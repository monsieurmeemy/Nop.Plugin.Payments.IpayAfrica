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
                MerchantKey = IpayAfricaPaymentSettings.MerchantKey
            };
            return View("~/Plugins/Payments.IpayAfrica/Views/Configure.cshtml", model);
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
                            //order note
                            order.OrderNotes.Add(new OrderNote
                            {
                                Note = "Thank you for shopping with us. Your " + channel + " transaction was successful. Your transaction code was " + transactinon_code,
                                DisplayToCustomer = true,
                                CreatedOnUtc = DateTime.UtcNow
                            });
                            //_orderService.UpdateOrder(order);
                            _orderProcessingService.MarkOrderAsPaid(order);
                        }
                        return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                    }
                    else
                    {
                        //order note
                        order.OrderNotes.Add(new OrderNote
                        {
                            Note = "Failed due to amount mismatch. Your attempt to pay via " + channel + " was successful. Your transaction code was " + transactinon_code,
                            DisplayToCustomer = true,
                            CreatedOnUtc = DateTime.UtcNow
                        });
                        //_orderService.UpdateOrder(order);
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
                //if (html.Contains("aei7p7yrx4ae34") || html.Contains("eq3i7p5yt7645e"))
                if (html.Contains("aei7p7yrx4ae34") || html.Contains("eq3i7p5yt7645e") && System.Convert.ToDecimal(paid_total) >= order.OrderTotal)
                {
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        //order note
                        order.OrderNotes.Add(new OrderNote
                        {
                            Note = "Thank you for shopping with us. Your " + channel + " transaction was successful. Your transaction code was " + transactinon_code,
                            DisplayToCustomer = true,
                            CreatedOnUtc = DateTime.UtcNow
                        });
                        //_orderService.UpdateOrder(order);
                        _orderProcessingService.MarkOrderAsPaid(order);
                    }
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                }
                else
                {
                    //order note
                    order.OrderNotes.Add(new OrderNote
                    {
                        Note = "Failed due to amount mismatch. You paid " + paid_total + " instead of " + order.OrderTotal + " via " + channel + " Your transaction code was " + transactinon_code,
                        DisplayToCustomer = true,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    _orderService.UpdateOrder(order);
                    return RedirectToRoute("OrderDetails", new { orderId = order.Id });
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
                if (html.Contains("aei7p7yrx4ae34") || html.Contains("eq3i7p5yt7645e") && System.Convert.ToDecimal(paid_total) >= order.OrderTotal)
                {
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        //order note
                        order.OrderNotes.Add(new OrderNote
                        {
                            Note = "Thank you for shopping with us. Your " + channel + " transaction was successful. Your transaction code was " + transactinon_code,
                            DisplayToCustomer = true,
                            CreatedOnUtc = DateTime.UtcNow
                        });
                        _orderProcessingService.MarkOrderAsPaid(order);
                    }
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
                }
                else
                {
                    //order note
                    order.OrderNotes.Add(new OrderNote
                    {
                        Note = "Failed due to amount mismatch. You paid " + paid_total + " instead of " + order.OrderTotal + " via " + channel + " Your transaction code was " + transactinon_code,
                        DisplayToCustomer = true,
                        CreatedOnUtc = DateTime.UtcNow
                    });
                    return RedirectToRoute("OrderDetails", new { orderId = order.Id });
                }
            }
        }

        private bool TxnStatus(string OrderId, String amount)
        {
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