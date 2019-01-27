using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.IpayAfrica.Models;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using System.Net;
using Microsoft.AspNetCore.Http;
using Nop.Services.Directory;
using Nop.Services.Tax;
using Nop.Core.Domain.Directory;
using System.Security.Cryptography;

namespace Nop.Plugin.Payments.IpayAfrica.Controllers
{
    public class PaymentIpayAfricaController : BasePaymentController
    {
        #region Fields
        private readonly CurrencySettings _currencySettings;
        private readonly IWorkContext _workContext;
        private readonly IStoreService _storeService;
        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IPermissionService _permissionService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly IStoreContext _storeContext;
        private readonly ILogger _logger;
        private readonly IWebHelper _webHelper;
        private readonly PaymentSettings _paymentSettings;
        private readonly IpayAfricaPaymentSettings _IpayAfricaPaymentSettings;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ITaxService _taxService;
        private readonly ICurrencyService _currencyService;
        #endregion

        #region Ctor

        public PaymentIpayAfricaController(IWorkContext workContext,
            IStoreService storeService,
            ISettingService settingService,
            IPaymentService paymentService,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IPermissionService permissionService,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IStoreContext storeContext,
            ILogger logger,
            IWebHelper webHelper,
            PaymentSettings paymentSettings,
            IpayAfricaPaymentSettings IpayAfricaPaymentSettings,
            ShoppingCartSettings shoppingCartSettings,
            CurrencySettings currencySettings,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICurrencyService currencyService,
            IHttpContextAccessor httpContextAccessor,
            ITaxService taxService)
        {
            this._workContext = workContext;
            this._storeService = storeService;
            this._settingService = settingService;
            this._paymentService = paymentService;
            this._orderService = orderService;
            this._orderProcessingService = orderProcessingService;
            this._permissionService = permissionService;
            this._genericAttributeService = genericAttributeService;
            this._localizationService = localizationService;
            this._storeContext = storeContext;
            this._logger = logger;
            this._webHelper = webHelper;
            this._paymentSettings = paymentSettings;
            this._IpayAfricaPaymentSettings = IpayAfricaPaymentSettings;
            this._shoppingCartSettings = shoppingCartSettings;
            this._currencySettings = currencySettings;
            this._checkoutAttributeParser = checkoutAttributeParser;
            this._currencyService = currencyService;
            this._genericAttributeService = genericAttributeService;
            this._httpContextAccessor = httpContextAccessor;
            this._taxService = taxService;
        }

        #endregion

        #region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult Configure()
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            //var storeScope = this.GetActiveStoreScopeConfiguration(_storeService, _workContext);
            var storeScope = _storeContext.ActiveStoreScopeConfiguration;
            var IpayAfricaPaymentSettings = _settingService.LoadSetting<IpayAfricaPaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                MerchantId = IpayAfricaPaymentSettings.MerchantId,
                MerchantKey = IpayAfricaPaymentSettings.MerchantKey,
                Website = IpayAfricaPaymentSettings.Website,
                IndustryTypeId = IpayAfricaPaymentSettings.IndustryTypeId,
                PaymentUrl = IpayAfricaPaymentSettings.PaymentUrl,
                CallBackUrl = IpayAfricaPaymentSettings.CallBackUrl,
                TxnStatusUrl = IpayAfricaPaymentSettings.TxnStatusUrl,
                UseDefaultCallBack = IpayAfricaPaymentSettings.UseDefaultCallBack
            };
            return View("~/Plugins/Payments.IpayAfrica/Views/Configure.cshtml", model);
        }

        private string GetStatusUrl()
        {
            return _IpayAfricaPaymentSettings.TxnStatusUrl;
        }

        [HttpPost]
        [AuthorizeAdmin]
        [AdminAntiForgery]
        [Area(AreaNames.Admin)]
        public IActionResult Configure(ConfigurationModel model)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return Configure();

            if (!ModelState.IsValid)
                return Configure();

            //save settings
            _IpayAfricaPaymentSettings.MerchantId = model.MerchantId;
            _IpayAfricaPaymentSettings.MerchantKey = model.MerchantKey;
            _IpayAfricaPaymentSettings.Website = model.Website;
            _IpayAfricaPaymentSettings.IndustryTypeId = model.IndustryTypeId;
            _IpayAfricaPaymentSettings.PaymentUrl = model.PaymentUrl;
            _IpayAfricaPaymentSettings.CallBackUrl = model.CallBackUrl;
            _IpayAfricaPaymentSettings.TxnStatusUrl = model.TxnStatusUrl;
            _IpayAfricaPaymentSettings.UseDefaultCallBack = model.UseDefaultCallBack;
            _settingService.SaveSetting(_IpayAfricaPaymentSettings);
            SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));
            return Configure();

        }

        public ActionResult Return()
        {
            var processor = _paymentService.LoadPaymentMethodBySystemName("Payments.IpayAfrica") as IpayAfricaPaymentProcessor;
            if (processor == null ||
                !_paymentService.IsPaymentMethodActive(processor) || !processor.PluginDescriptor.Installed)
                throw new NopException("IpayAfrica module cannot be loaded");


            var myUtility = new IpayAfricaHelper();
            string orderId, Amount, AuthDesc, ResCode;
            bool checkSumMatch = false;
            //Assign following values to send it to verifychecksum function.
            if (String.IsNullOrWhiteSpace(_IpayAfricaPaymentSettings.MerchantKey))
                throw new NopException("IpayAfrica key is not set");

            string workingKey = _IpayAfricaPaymentSettings.MerchantKey;
            string IpayAfricaChecksum = null;
            string transactinon_code = HttpContext.Request.Query["txncd"];
            string qwh = HttpContext.Request.Query["qwh"];
            string afd = HttpContext.Request.Query["afd"];
            string agt = HttpContext.Request.Query["agt"];
            string uyt = HttpContext.Request.Query["uyt"];
            string ifd = HttpContext.Request.Query["ifd"];
            string poi = HttpContext.Request.Query["poi"];
            string returned_order_id = HttpContext.Request.Query["id"];
            string returned_order_invoice = HttpContext.Request.Query["ivm"];
            string status = HttpContext.Request.Query["status"];
            string paid_total = HttpContext.Request.Query["mc"] + "00";
            string p1 = HttpContext.Request.Query["p1"];
            string p2 = HttpContext.Request.Query["p2"];
            string p3 = HttpContext.Request.Query["p3"];
            string p4 = HttpContext.Request.Query["p4"];
            string msisdn_id = HttpContext.Request.Query["msisdn_id"];
            string msisdn_idnum = HttpContext.Request.Query["msisdn_idnum"];
            string channel = HttpContext.Request.Query["channel"];
            string hash_id = HttpContext.Request.Query["hsh"];

            Dictionary<string, string> parameters = new Dictionary<string, string>();
  
            Dictionary<string, string> parameters1 = new Dictionary<string, string>
            {
                ["txncd"] = transactinon_code,
                ["qwh"] = qwh,
                ["afd"] = afd,
                ["poi"] = poi,
                ["uyt"] = uyt,
                ["ifd"] = ifd,
                ["agt"] = agt,
                ["id"] = returned_order_id,
                ["status"] = status,
                ["ivm"] = returned_order_invoice,
                ["mc"] = paid_total,
                ["p1"] = p1,
                ["p2"] = p2,
                ["p3"] = p3,
                ["p4"] = p4,
                ["msisdn_id"] = p1,
                ["msisdn_idnum"] = p2,
                ["channel"] = p3,
                ["p4"] = p4,
                ["hsh"] = HttpContext.Request.Query["hsh"]
            };
     
            var live = "1";
            string key = _IpayAfricaPaymentSettings.MerchantKey;
            var storeLocation = _webHelper.GetStoreLocation();
            string vendor_id = _IpayAfricaPaymentSettings.MerchantId;
            string currency = p3;
            string customer_email = p1;
            string mobile_number = msisdn_idnum;
            string callback_url = p2;
            string email_notify = "1";
            string curl = p4;
            if (mobile_number.Length > 10)
            {
                mobile_number = mobile_number.Remove(0, 3).Insert(0, "0");
            }

            string datastring = live.ToString() + returned_order_id + returned_order_invoice + paid_total + mobile_number + customer_email + vendor_id + currency + p1 + p2 + p3 + p4 + callback_url + email_notify + curl;
            byte[] keyByte = new ASCIIEncoding().GetBytes(key);
            byte[] messageBytes = new ASCIIEncoding().GetBytes(datastring);
            byte[] hashmessage = new HMACSHA1(keyByte).ComputeHash(messageBytes);
            String.Concat(Array.ConvertAll(hashmessage, x => x.ToString("x2")));
            string hash_message = String.Concat(Array.ConvertAll(hashmessage, x => x.ToString("x2")));

            if (hash_id != null)
            {
                IpayAfricaChecksum = hash_id;
            }
            if (IpayAfricaChecksum == String.Concat(Array.ConvertAll(hashmessage, x => x.ToString("x2"))))
            {
                checkSumMatch = true;
            }
            
            orderId = returned_order_id;
            Amount = HttpContext.Request.Query["mc"];
            ResCode = returned_order_invoice;
            AuthDesc = status;

            var order = _orderService.GetOrderById(Convert.ToInt32(orderId));
            if (checkSumMatch == true)
            {
                if (AuthDesc == "aei7p7yrx4ae34")
                {
                    string ipnurl2 = "https://www.ipayafrica.com/ipn/?vendor=" + _IpayAfricaPaymentSettings.MerchantId + "&id=" + HttpContext.Request.Query["id"] + "&ivm=" + HttpContext.Request.Query["ivm"] + "&qwh=" + HttpContext.Request.Query["qwh"] + "&afd=" + HttpContext.Request.Query["afd"] + "&poi=" + HttpContext.Request.Query["poi"] + "&uyt=" + HttpContext.Request.Query["uyt"] + "&ifd=" + HttpContext.Request.Query["ifd"];

                    string html = string.Empty;
                    string url = ipnurl2;

                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        html = reader.ReadToEnd();
                    }
                    if (TxnStatus(orderId, order.OrderTotal.ToString("0.00")))
                    {
                        if (_orderProcessingService.CanMarkOrderAsPaid(order))
                        {
                            _orderProcessingService.MarkOrderAsPaid(order);
                        }
                        return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                    }
                    else
                    {
                        return Content("Amount Mismatch" + " " + html + " " + order.OrderTotal.ToString());
                    }
                }
                else if (AuthDesc == "fe2707etr5s4wq")
                {
                    _orderProcessingService.CancelOrder(order, false);
                    order.OrderStatus = OrderStatus.Cancelled;
                    _orderService.UpdateOrder(order);
                    return RedirectToRoute("OrderDetails", new { orderId = order.Id });
                }
                else
                {
                    return Content("Security Error. Illegal access detected. Please try again");
                }
            }
            else if (string.IsNullOrEmpty(IpayAfricaChecksum))
            {
                return Content("Please Contact Customer Care");
            }
            else if (status == "dtfi4p7yty45wq")//less paid
            {
                return Content("Payment Failed. You Paid less than what was requested");
            }
            else if (status == "eq3i7p5yt7645e")//more paid
            {
                string ipnurl2 = "https://www.ipayafrica.com/ipn/?vendor=" + _IpayAfricaPaymentSettings.MerchantId + "&id=" + HttpContext.Request.Query["id"] + "&ivm=" + HttpContext.Request.Query["ivm"] + "&qwh=" + HttpContext.Request.Query["qwh"] + "&afd=" + HttpContext.Request.Query["afd"] + "&poi=" + HttpContext.Request.Query["poi"] + "&uyt=" + HttpContext.Request.Query["uyt"] + "&ifd=" + HttpContext.Request.Query["ifd"];

                string html = string.Empty;
                string url = ipnurl2;

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    html = reader.ReadToEnd();
                }
                if (html.Contains("aei7p7yrx4ae34") || html.Contains("eq3i7p5yt7645e"))
                {
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        _orderProcessingService.MarkOrderAsPaid(order);
                    }
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                }
                else
                {
                    return Content("Amount Mismatch");
                }
            }
            else if (status == "bdi6p2yy76etrs")//pending
            {
                return RedirectToRoute("OrderDetails", new { orderId = order.Id });
            }
            else if(status == "fe2707etr5s4wq")//failed
            {
                //return Content("Security Error. Illegal access detected, Checksum failed");
                if (_orderProcessingService.CanVoidOffline(order))
                {
                    _orderProcessingService.VoidOffline(order);
                }
                return RedirectToRoute("OrderDetails", new { orderId = order.Id});
            }
            else
            {
                //if(status == "")
                //{

                //}
                string ipnurl2 = "https://www.ipayafrica.com/ipn/?vendor=" + _IpayAfricaPaymentSettings.MerchantId + "&id=" + HttpContext.Request.Query["id"] + "&ivm=" + HttpContext.Request.Query["ivm"] + "&qwh=" + HttpContext.Request.Query["qwh"] + "&afd=" + HttpContext.Request.Query["afd"] + "&poi=" + HttpContext.Request.Query["poi"] + "&uyt=" + HttpContext.Request.Query["uyt"] + "&ifd=" + HttpContext.Request.Query["ifd"];

                string html = string.Empty;
                string url = ipnurl2;

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    html = reader.ReadToEnd();
                }
                if (html.Contains("aei7p7yrx4ae34") || html.Contains("eq3i7p5yt7645e"))
                {
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        _orderProcessingService.MarkOrderAsPaid(order);
                    }
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                }
                else
                {
                    return Content("Amount Mismatch");
                }
                //return RedirectToRoute("OrderDetails", new { orderId = order.Id });
            }
        }

        private bool TxnStatus(string OrderId, String amount)
        {
            String uri = GetStatusUrl();
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            parameters.Add("vendor", _IpayAfricaPaymentSettings.MerchantId);
            parameters.Add("id", OrderId);
            parameters.Add("ivm", HttpContext.Request.Query["ivm"]);
            parameters.Add("qwh", HttpContext.Request.Query["qwh"]);
            parameters.Add("afd", HttpContext.Request.Query["afd"]);
            parameters.Add("poi", HttpContext.Request.Query["poi"]);
            parameters.Add("uyt", HttpContext.Request.Query["uyt"]);
            parameters.Add("ifd", HttpContext.Request.Query["ifd"]);

            string ipnurl2 = "https://www.ipayafrica.com/ipn/?vendor=" + _IpayAfricaPaymentSettings.MerchantId + "&id=" + HttpContext.Request.Query["id"] + "&ivm=" + HttpContext.Request.Query["ivm"] + "&qwh=" + HttpContext.Request.Query["qwh"] + "&afd=" + HttpContext.Request.Query["afd"] + "&poi=" + HttpContext.Request.Query["poi"] + "&uyt=" + HttpContext.Request.Query["uyt"] + "&ifd=" + HttpContext.Request.Query["ifd"];

            string checksum = HttpContext.Request.Query["hsh"];

            try
            {
                string postData = "{\"vendor\":\"" + _IpayAfricaPaymentSettings.MerchantId + "\",\"id\":\"" + OrderId + "\",\"ivm\":\"" + HttpContext.Request.Query["ivm"] + OrderId + "\",\"qwh\":\"" + HttpContext.Request.Query["qwh"] + "\",\"afd\":\"" + HttpContext.Request.Query["afd"] + "\",\"poi\":\"" + HttpContext.Request.Query["poi"] + "\",\"uyt\":\"" + HttpContext.Request.Query["uyt"] + "\",\"ifd\":\"" + HttpContext.Request.Query["ifd"] + "\"}";

                HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(uri);
                webRequest.Method = "POST";
                webRequest.Accept = "text/html";
                webRequest.ContentType = "text/html";
                string html = string.Empty;
                string url = ipnurl2;

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    html = reader.ReadToEnd();

                    if (html.Contains("aei7p7yrx4ae34"))
                    {
                        return true;
                    }
                    else
                    {

                        //
                    }
                }
            }
            catch (Exception ex)
            {

            }
            return false;
        }

        //action displaying notification (warning) to a store owner about inaccurate IpayAfrica rounding
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public IActionResult RoundingWarning(bool passProductNamesAndTotals)
        {
            if (!_permissionService.Authorize(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //prices and total aren't rounded, so display warning
            if (passProductNamesAndTotals && !_shoppingCartSettings.RoundPricesDuringCalculation)
                return Json(new { Result = _localizationService.GetResource("Plugins.Payments.IpayAfrica.RoundingWarning") });

            return Json(new { Result = string.Empty });
        }

        public IActionResult PDTHandler()
        {
            var tx = _webHelper.QueryString<string>("tx");

            var processor = _paymentService.LoadPaymentMethodBySystemName("Payments.IpayAfrica") as IpayAfricaPaymentProcessor;
            if (processor == null ||
                !_paymentService.IsPaymentMethodActive(processor) || !processor.PluginDescriptor.Installed)
                throw new NopException("IpayAfrica Standard module cannot be loaded");

            if (processor.GetPdtDetails(tx, out Dictionary<string, string> values, out string response))
            {
                values.TryGetValue("custom", out string orderNumber);
                var orderNumberGuid = Guid.Empty;
                try
                {
                    orderNumberGuid = new Guid(orderNumber);
                }
                catch { }
                var order = _orderService.GetOrderByGuid(orderNumberGuid);
                if (order != null)
                {
                    var mc_gross = decimal.Zero;
                    try
                    {
                        mc_gross = decimal.Parse(values["mc_gross"], new CultureInfo("en-US"));
                    }
                    catch (Exception exc)
                    {
                        _logger.Error("IpayAfrica PDT. Error getting mc_gross", exc);
                    }

                    values.TryGetValue("payer_status", out string payer_status);
                    values.TryGetValue("payment_status", out string payment_status);
                    values.TryGetValue("pending_reason", out string pending_reason);
                    values.TryGetValue("mc_currency", out string mc_currency);
                    values.TryGetValue("txn_id", out string txn_id);
                    values.TryGetValue("payment_type", out string payment_type);
                    values.TryGetValue("payer_id", out string payer_id);
                    values.TryGetValue("receiver_id", out string receiver_id);
                    values.TryGetValue("invoice", out string invoice);
                    values.TryGetValue("payment_fee", out string payment_fee);

                    var sb = new StringBuilder();
                    sb.AppendLine("IpayAfrica PDT:");
                    sb.AppendLine("mc_gross: " + mc_gross);
                    sb.AppendLine("Payer status: " + payer_status);
                    sb.AppendLine("Payment status: " + payment_status);
                    sb.AppendLine("Pending reason: " + string.Empty);
                    sb.AppendLine("mc_currency: " + mc_currency);
                    sb.AppendLine("txn_id: " + txn_id);
                    sb.AppendLine("payment_type: " + payment_type);
                    sb.AppendLine("payer_id: " + payer_id);
                    sb.AppendLine("receiver_id: " + receiver_id);
                    sb.AppendLine("invoice: " + invoice);
                    sb.AppendLine("payment_fee: " + payment_fee);

                    var newPaymentStatus = IpayAfricaHelper.GetPaymentStatus(payment_status, string.Empty);
                    sb.AppendLine("New payment status: " + newPaymentStatus);

                    //order note
                    order.OrderNotes.Add(new OrderNote
                    {
                        Note = sb.ToString(),
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    _orderService.UpdateOrder(order);

                    //validate order total
                    var orderTotalSentToIpayAfrica = _genericAttributeService.GetAttribute<decimal?>(order, IpayAfricaHelper.OrderTotalSentToIpayAfrica);
                    if (orderTotalSentToIpayAfrica.HasValue && mc_gross != orderTotalSentToIpayAfrica.Value)
                    {
                        var errorStr =
                            $"IpayAfrica PDT. Returned order total {mc_gross} doesn't equal order total {order.OrderTotal}. Order# {order.Id}.";
                        //log
                        _logger.Error(errorStr);
                        //order note
                        order.OrderNotes.Add(new OrderNote
                        {
                            Note = errorStr,
                            DisplayToCustomer = false,
                            CreatedOnUtc = DateTime.UtcNow
                        });
                        _orderService.UpdateOrder(order);

                        return RedirectToAction("Index", "Home", new { area = "" });
                    }
                    //clear attribute
                    if (orderTotalSentToIpayAfrica.HasValue)
                        _genericAttributeService.SaveAttribute<decimal?>(order, IpayAfricaHelper.OrderTotalSentToIpayAfrica, null);

                    //mark order as paid
                    if (newPaymentStatus == PaymentStatus.Paid)
                    {
                        if (_orderProcessingService.CanMarkOrderAsPaid(order))
                        {
                            order.AuthorizationTransactionId = txn_id;
                            _orderService.UpdateOrder(order);

                            _orderProcessingService.MarkOrderAsPaid(order);
                        }
                    }
                }

                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }
            else
            {
                var orderNumber = string.Empty;
                values.TryGetValue("custom", out orderNumber);
                var orderNumberGuid = Guid.Empty;
                try
                {
                    orderNumberGuid = new Guid(orderNumber);
                }
                catch { }
                var order = _orderService.GetOrderByGuid(orderNumberGuid);
                if (order != null)
                {
                    //order note
                    order.OrderNotes.Add(new OrderNote
                    {
                        Note = "IpayAfrica PDT failed. " + response,
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    _orderService.UpdateOrder(order);
                }
                return RedirectToAction("Index", "Home", new { area = "" });
            }
        }

        public IActionResult IPNHandler()
        {
            byte[] parameters;
            using (var stream = new MemoryStream())
            {
                this.Request.Body.CopyTo(stream);
                parameters = stream.ToArray();
            }
            var strRequest = Encoding.ASCII.GetString(parameters);

            var processor = _paymentService.LoadPaymentMethodBySystemName("Payments.IpayAfrica") as IpayAfricaPaymentProcessor;
            if (processor == null ||
                !_paymentService.IsPaymentMethodActive(processor) || !processor.PluginDescriptor.Installed)
                throw new NopException("IpayAfrica Standard module cannot be loaded");

            if (processor.VerifyIpn(strRequest, out Dictionary<string, string> values))
            {
                #region values
                var mc_gross = decimal.Zero;
                try
                {
                    mc_gross = decimal.Parse(values["mc_gross"], new CultureInfo("en-US"));
                }
                catch { }

                values.TryGetValue("payer_status", out string payer_status);
                values.TryGetValue("payment_status", out string payment_status);
                values.TryGetValue("pending_reason", out string pending_reason);
                values.TryGetValue("mc_currency", out string mc_currency);
                values.TryGetValue("txn_id", out string txn_id);
                values.TryGetValue("txn_type", out string txn_type);
                values.TryGetValue("rp_invoice_id", out string rp_invoice_id);
                values.TryGetValue("payment_type", out string payment_type);
                values.TryGetValue("payer_id", out string payer_id);
                values.TryGetValue("receiver_id", out string receiver_id);
                values.TryGetValue("invoice", out string _);
                values.TryGetValue("payment_fee", out string payment_fee);

                #endregion

                var sb = new StringBuilder();
                sb.AppendLine("IpayAfrica IPN:");
                foreach (var kvp in values)
                {
                    sb.AppendLine(kvp.Key + ": " + kvp.Value);
                }

                var newPaymentStatus = IpayAfricaHelper.GetPaymentStatus(payment_status, pending_reason);
                sb.AppendLine("New payment status: " + newPaymentStatus);

                switch (txn_type)
                {
                    case "recurring_payment_profile_created":
                        //do nothing here
                        break;
                    #region Recurring payment
                    case "recurring_payment":
                        {
                            var orderNumberGuid = Guid.Empty;
                            try
                            {
                                orderNumberGuid = new Guid(rp_invoice_id);
                            }
                            catch
                            {
                            }

                            var initialOrder = _orderService.GetOrderByGuid(orderNumberGuid);
                            if (initialOrder != null)
                            {
                                var recurringPayments = _orderService.SearchRecurringPayments(initialOrderId: initialOrder.Id);
                                foreach (var rp in recurringPayments)
                                {
                                    switch (newPaymentStatus)
                                    {
                                        case PaymentStatus.Authorized:
                                        case PaymentStatus.Paid:
                                            {
                                                var recurringPaymentHistory = rp.RecurringPaymentHistory;
                                                if (!recurringPaymentHistory.Any())
                                                {
                                                    //first payment
                                                    var rph = new RecurringPaymentHistory
                                                    {
                                                        RecurringPaymentId = rp.Id,
                                                        OrderId = initialOrder.Id,
                                                        CreatedOnUtc = DateTime.UtcNow
                                                    };
                                                    rp.RecurringPaymentHistory.Add(rph);
                                                    _orderService.UpdateRecurringPayment(rp);
                                                }
                                                else
                                                {
                                                    //next payments
                                                    var processPaymentResult = new ProcessPaymentResult
                                                    {
                                                        NewPaymentStatus = newPaymentStatus
                                                    };
                                                    if (newPaymentStatus == PaymentStatus.Authorized)
                                                        processPaymentResult.AuthorizationTransactionId = txn_id;
                                                    else
                                                        processPaymentResult.CaptureTransactionId = txn_id;

                                                    _orderProcessingService.ProcessNextRecurringPayment(rp, processPaymentResult);
                                                }
                                            }
                                            break;
                                        case PaymentStatus.Voided:
                                            //failed payment
                                            var failedPaymentResult = new ProcessPaymentResult
                                            {
                                                Errors = new[] { $"IpayAfrica IPN. Recurring payment is {payment_status} ." },
                                                RecurringPaymentFailed = true
                                            };
                                            _orderProcessingService.ProcessNextRecurringPayment(rp, failedPaymentResult);
                                            break;
                                    }
                                }

                                //this.OrderService.InsertOrderNote(newOrder.OrderId, sb.ToString(), DateTime.UtcNow);
                                _logger.Information("IpayAfrica IPN. Recurring info", new NopException(sb.ToString()));
                            }
                            else
                            {
                                _logger.Error("IpayAfrica IPN. Order is not found", new NopException(sb.ToString()));
                            }
                        }
                        break;
                    case "recurring_payment_failed":
                        if (Guid.TryParse(rp_invoice_id, out Guid orderGuid))
                        {
                            var initialOrder = _orderService.GetOrderByGuid(orderGuid);
                            if (initialOrder != null)
                            {
                                var recurringPayment = _orderService.SearchRecurringPayments(initialOrderId: initialOrder.Id).FirstOrDefault();
                                //failed payment
                                if (recurringPayment != null)
                                    _orderProcessingService.ProcessNextRecurringPayment(recurringPayment, new ProcessPaymentResult { Errors = new[] { txn_type }, RecurringPaymentFailed = true });
                            }
                        }
                        break;
                    #endregion
                    default:
                        #region Standard payment
                        {
                            values.TryGetValue("custom", out string orderNumber);
                            var orderNumberGuid = Guid.Empty;
                            try
                            {
                                orderNumberGuid = new Guid(orderNumber);
                            }
                            catch
                            {
                            }

                            var order = _orderService.GetOrderByGuid(orderNumberGuid);
                            if (order != null)
                            {

                                //order note
                                order.OrderNotes.Add(new OrderNote
                                {
                                    Note = sb.ToString(),
                                    DisplayToCustomer = false,
                                    CreatedOnUtc = DateTime.UtcNow
                                });
                                _orderService.UpdateOrder(order);

                                switch (newPaymentStatus)
                                {
                                    case PaymentStatus.Pending:
                                        {
                                        }
                                        break;
                                    case PaymentStatus.Authorized:
                                        {
                                            //validate order total
                                            if (Math.Round(mc_gross, 2).Equals(Math.Round(order.OrderTotal, 2)))
                                            {
                                                //valid
                                                if (_orderProcessingService.CanMarkOrderAsAuthorized(order))
                                                {
                                                    _orderProcessingService.MarkAsAuthorized(order);
                                                }
                                            }
                                            else
                                            {
                                                //not valid
                                                var errorStr =
                                                    $"IpayAfrica IPN. Returned order total {mc_gross} doesn't equal order total {order.OrderTotal}. Order# {order.Id}.";
                                                //log
                                                _logger.Error(errorStr);
                                                //order note
                                                order.OrderNotes.Add(new OrderNote
                                                {
                                                    Note = errorStr,
                                                    DisplayToCustomer = false,
                                                    CreatedOnUtc = DateTime.UtcNow
                                                });
                                                _orderService.UpdateOrder(order);
                                            }
                                        }
                                        break;
                                    case PaymentStatus.Paid:
                                        {
                                            //validate order total
                                            if (Math.Round(mc_gross, 2).Equals(Math.Round(order.OrderTotal, 2)))
                                            {
                                                //valid
                                                if (_orderProcessingService.CanMarkOrderAsPaid(order))
                                                {
                                                    order.AuthorizationTransactionId = txn_id;
                                                    _orderService.UpdateOrder(order);

                                                    _orderProcessingService.MarkOrderAsPaid(order);
                                                }
                                            }
                                            else
                                            {
                                                //not valid
                                                var errorStr =
                                                    $"IpayAfrica IPN. Returned order total {mc_gross} doesn't equal order total {order.OrderTotal}. Order# {order.Id}.";
                                                //log
                                                _logger.Error(errorStr);
                                                //order note
                                                order.OrderNotes.Add(new OrderNote
                                                {
                                                    Note = errorStr,
                                                    DisplayToCustomer = false,
                                                    CreatedOnUtc = DateTime.UtcNow
                                                });
                                                _orderService.UpdateOrder(order);
                                            }
                                        }
                                        break;
                                    case PaymentStatus.Refunded:
                                        {
                                            var totalToRefund = Math.Abs(mc_gross);
                                            if (totalToRefund > 0 && Math.Round(totalToRefund, 2).Equals(Math.Round(order.OrderTotal, 2)))
                                            {
                                                //refund
                                                if (_orderProcessingService.CanRefundOffline(order))
                                                {
                                                    _orderProcessingService.RefundOffline(order);
                                                }
                                            }
                                            else
                                            {
                                                //partial refund
                                                if (_orderProcessingService.CanPartiallyRefundOffline(order, totalToRefund))
                                                {
                                                    _orderProcessingService.PartiallyRefundOffline(order, totalToRefund);
                                                }
                                            }
                                        }
                                        break;
                                    case PaymentStatus.Voided:
                                        {
                                            if (_orderProcessingService.CanVoidOffline(order))
                                            {
                                                _orderProcessingService.VoidOffline(order);
                                            }
                                        }
                                        break;
                                    default:
                                        break;
                                }
                            }
                            else
                            {
                                _logger.Error("IpayAfrica IPN. Order is not found", new NopException(sb.ToString()));
                            }
                        }
                        #endregion
                        break;
                }
            }
            else
            {
                _logger.Error("IpayAfrica IPN failed.", new NopException(strRequest));
            }

            //nothing should be rendered to visitor
            return Content("");
        }

        public IActionResult CancelOrder()
        {
            var order = _orderService.SearchOrders(storeId: _storeContext.CurrentStore.Id,
                customerId: _workContext.CurrentCustomer.Id, pageSize: 1).FirstOrDefault();
            if (order != null)
                return RedirectToRoute("OrderDetails", new { orderId = order.Id });

            return RedirectToRoute("HomePage");
        }

        #endregion
    }
}