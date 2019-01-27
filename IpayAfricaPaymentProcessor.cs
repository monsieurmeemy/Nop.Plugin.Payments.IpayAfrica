using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Services.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Plugins;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Tax;
using System.Security.Cryptography;

namespace Nop.Plugin.Payments.IpayAfrica
{
    /// <summary>
    /// IpayAfrica payment processor
    /// </summary>
    public class IpayAfricaPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly ICurrencyService _currencyService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IPaymentService _paymentService;

        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly ISettingService _settingService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly IpayAfricaPaymentSettings _IpayAfricaPaymentSettings;

        #endregion

        #region Ctor

        public IpayAfricaPaymentProcessor(CurrencySettings currencySettings,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICurrencyService currencyService,
            IGenericAttributeService genericAttributeService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IPaymentService paymentService,
            IOrderTotalCalculationService orderTotalCalculationService,
            ISettingService settingService,
            ITaxService taxService,
            IWebHelper webHelper,
            IpayAfricaPaymentSettings IpayAfricaPaymentSettings)
        {
            this._currencySettings = currencySettings;
            this._checkoutAttributeParser = checkoutAttributeParser;
            this._currencyService = currencyService;
            this._genericAttributeService = genericAttributeService;
            this._httpContextAccessor = httpContextAccessor;
            this._localizationService = localizationService;
            this._paymentService = paymentService;
            this._orderTotalCalculationService = orderTotalCalculationService;
            this._settingService = settingService;
            this._taxService = taxService;
            this._webHelper = webHelper;
            this._IpayAfricaPaymentSettings = IpayAfricaPaymentSettings;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Gets IpayAfrica URL
        /// </summary>
        /// <returns></returns>
        private string GetIpayAfricaUrl()
        {
            return "https://payments.ipayafrica.com/v3/ke";
        }

        /// <summary>
        /// Gets IPN IpayAfrica URL
        /// </summary>
        /// <returns></returns>
        private string GetIpnIpayAfricaUrl()
        {
            return "https://www.ipayafrica.com/ipn/";
        }

        /// <summary>
        /// Gets PDT details
        /// </summary>
        /// <param name="tx">TX</param>
        /// <param name="values">Values</param>
        /// <param name="response">Response</param>
        /// <returns>Result</returns>
        public bool GetPdtDetails(string tx, out Dictionary<string, string> values, out string response)
        {
            var req = (HttpWebRequest)WebRequest.Create(GetIpayAfricaUrl());
            req.Method = WebRequestMethods.Http.Post;
            req.ContentType = MimeTypes.ApplicationXWwwFormUrlencoded;
            //now IpayAfrica requires user-agent. otherwise, we can get 403 error
            req.UserAgent = _httpContextAccessor.HttpContext.Request.Headers[HeaderNames.UserAgent];

            var formContent = $"cmd=_notify-synch&at={_IpayAfricaPaymentSettings.PdtToken}&tx={tx}";
            req.ContentLength = formContent.Length;

            using (var sw = new StreamWriter(req.GetRequestStream(), Encoding.ASCII))
                sw.Write(formContent);

            using (var sr = new StreamReader(req.GetResponse().GetResponseStream()))
                response = WebUtility.UrlDecode(sr.ReadToEnd());

            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool firstLine = true, success = false;
            foreach (var l in response.Split('\n'))
            {
                var line = l.Trim();
                if (firstLine)
                {
                    success = line.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase);
                    firstLine = false;
                }
                else
                {
                    var equalPox = line.IndexOf('=');
                    if (equalPox >= 0)
                        values.Add(line.Substring(0, equalPox), line.Substring(equalPox + 1));
                }
            }

            return success;
        }

        /// <summary>
        /// Verifies IPN
        /// </summary>
        /// <param name="formString">Form string</param>
        /// <param name="values">Values</param>
        /// <returns>Result</returns>
        public bool VerifyIpn(string formString, out Dictionary<string, string> values)
        {
            var req = (HttpWebRequest)WebRequest.Create(GetIpnIpayAfricaUrl());
            req.Method = WebRequestMethods.Http.Post;
            req.ContentType = MimeTypes.ApplicationXWwwFormUrlencoded;
            //now IpayAfrica requires user-agent. otherwise, we can get 403 error
            req.UserAgent = _httpContextAccessor.HttpContext.Request.Headers[HeaderNames.UserAgent];

            var formContent = $"cmd=_notify-validate&{formString}";
            req.ContentLength = formContent.Length;

            using (var sw = new StreamWriter(req.GetRequestStream(), Encoding.ASCII))
            {
                sw.Write(formContent);
            }

            string response;
            using (var sr = new StreamReader(req.GetResponse().GetResponseStream()))
            {
                response = WebUtility.UrlDecode(sr.ReadToEnd());
            }
            var success = response.Trim().Equals("VERIFIED", StringComparison.OrdinalIgnoreCase);

            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in formString.Split('&'))
            {
                var line = l.Trim();
                var equalPox = line.IndexOf('=');
                if (equalPox >= 0)
                    values.Add(line.Substring(0, equalPox), line.Substring(equalPox + 1));
            }

            return success;
        }

        /// <summary>
        /// Create common query parameters for the request
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Created query parameters</returns>
        /*private IDictionary<string, string> CreateQueryParameters(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //get store location
            var storeLocation = _webHelper.GetStoreLocation();

            //create query parameters
            return new Dictionary<string, string>
            {
                //IpayAfrica ID or an email address associated with your IpayAfrica account
                //["business"] = _IpayAfricaPaymentSettings.BusinessEmail, //mayank

                //the character set and character encoding
                ["charset"] = "utf-8",

                //set return method to "2" (the customer redirected to the return URL by using the POST method, and all payment variables are included)
                ["rm"] = "2",

                ["bn"] = IpayAfricaHelper.NopCommercePartnerCode,
                ["currency_code"] = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId)?.CurrencyCode,

                //order identifier
                ["invoice"] = postProcessPaymentRequest.Order.CustomOrderNumber,
                ["custom"] = postProcessPaymentRequest.Order.OrderGuid.ToString(),

                //PDT, IPN and cancel URL
                ["return"] = $"{storeLocation}Plugins/PaymentIpayAfrica/PDTHandler",
                ["notify_url"] = $"{storeLocation}Plugins/PaymentIpayAfrica/IPNHandler",
                ["cancel_return"] = $"{storeLocation}Plugins/PaymentIpayAfrica/CancelOrder",

                //shipping address, if exists
                ["no_shipping"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "1" : "2",
                ["address_override"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "0" : "1",
                ["first_name"] = postProcessPaymentRequest.Order.ShippingAddress?.FirstName,
                ["last_name"] = postProcessPaymentRequest.Order.ShippingAddress?.LastName,
                ["address1"] = postProcessPaymentRequest.Order.ShippingAddress?.Address1,
                ["address2"] = postProcessPaymentRequest.Order.ShippingAddress?.Address2,
                ["city"] = postProcessPaymentRequest.Order.ShippingAddress?.City,
                ["state"] = postProcessPaymentRequest.Order.ShippingAddress?.StateProvince?.Abbreviation,
                ["country"] = postProcessPaymentRequest.Order.ShippingAddress?.Country?.TwoLetterIsoCode,
                ["zip"] = postProcessPaymentRequest.Order.ShippingAddress?.ZipPostalCode,
                ["email"] = postProcessPaymentRequest.Order.ShippingAddress?.Email
            };
        }
        */

        /// <summary>
        /// Add order items to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /*private void AddItemsParameters(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //upload order items
            parameters.Add("cmd", "_cart");
            parameters.Add("upload", "1");

            var cartTotal = decimal.Zero;
            var roundedCartTotal = decimal.Zero;
            var itemCount = 1;

            //add shopping cart items
            foreach (var item in postProcessPaymentRequest.Order.OrderItems)
            {
                var roundedItemPrice = Math.Round(item.UnitPriceExclTax, 2);

                //add query parameters
                parameters.Add($"item_name_{itemCount}", item.Product.Name);
                parameters.Add($"amount_{itemCount}", roundedItemPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", item.Quantity.ToString());

                cartTotal += item.PriceExclTax;
                roundedCartTotal += roundedItemPrice * item.Quantity;
                itemCount++;
            }

            //add checkout attributes as order items
            var checkoutAttributeValues = _checkoutAttributeParser.ParseCheckoutAttributeValues(postProcessPaymentRequest.Order.CheckoutAttributesXml);
            foreach (var attributeValue in checkoutAttributeValues)
            {
                var attributePrice = _taxService.GetCheckoutAttributePrice(attributeValue, false, postProcessPaymentRequest.Order.Customer);
                var roundedAttributePrice = Math.Round(attributePrice, 2);

                //add query parameters
                if (attributeValue.CheckoutAttribute != null)
                {
                    parameters.Add($"item_name_{itemCount}", attributeValue.CheckoutAttribute.Name);
                    parameters.Add($"amount_{itemCount}", roundedAttributePrice.ToString("0.00", CultureInfo.InvariantCulture));
                    parameters.Add($"quantity_{itemCount}", "1");

                    cartTotal += attributePrice;
                    roundedCartTotal += roundedAttributePrice;
                    itemCount++;
                }
            }

            //add shipping fee as a separate order item, if it has price
            var roundedShippingPrice = Math.Round(postProcessPaymentRequest.Order.OrderShippingExclTax, 2);
            if (roundedShippingPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Shipping fee");
                parameters.Add($"amount_{itemCount}", roundedShippingPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.OrderShippingExclTax;
                roundedCartTotal += roundedShippingPrice;
                itemCount++;
            }

            //add payment method additional fee as a separate order item, if it has price
            var roundedPaymentMethodPrice = Math.Round(postProcessPaymentRequest.Order.PaymentMethodAdditionalFeeExclTax, 2);
            if (roundedPaymentMethodPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Payment method fee");
                parameters.Add($"amount_{itemCount}", roundedPaymentMethodPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.PaymentMethodAdditionalFeeExclTax;
                roundedCartTotal += roundedPaymentMethodPrice;
                itemCount++;
            }

            //add tax as a separate order item, if it has positive amount
            var roundedTaxAmount = Math.Round(postProcessPaymentRequest.Order.OrderTax, 2);
            if (roundedTaxAmount > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Tax amount");
                parameters.Add($"amount_{itemCount}", roundedTaxAmount.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.OrderTax;
                roundedCartTotal += roundedTaxAmount;
                itemCount++;
            }

            if (cartTotal > postProcessPaymentRequest.Order.OrderTotal)
            {
                //get the difference between what the order total is and what it should be and use that as the "discount"
                var discountTotal = Math.Round(cartTotal - postProcessPaymentRequest.Order.OrderTotal, 2);
                roundedCartTotal -= discountTotal;

                //gift card or rewarded point amount applied to cart in nopCommerce - shows in IpayAfrica as "discount"
                parameters.Add("discount_amount_cart", discountTotal.ToString("0.00", CultureInfo.InvariantCulture));
            }

            //save order total that actually sent to IpayAfrica (used for PDT order total validation)
            _genericAttributeService.SaveAttribute(postProcessPaymentRequest.Order, IpayAfricaHelper.OrderTotalSentToIpayAfrica, roundedCartTotal);
        }
        */
        /// <summary>
        /// Add order total to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /*private void AddOrderTotalParameters(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //round order total
            var roundedOrderTotal = Math.Round(postProcessPaymentRequest.Order.OrderTotal, 2);

            parameters.Add("cmd", "_xclick");
            parameters.Add("item_name", $"Order Number {postProcessPaymentRequest.Order.CustomOrderNumber}");
            parameters.Add("amount", roundedOrderTotal.ToString("0.00", CultureInfo.InvariantCulture));

            //save order total that actually sent to IpayAfrica (used for PDT order total validation)
            _genericAttributeService.SaveAttribute(postProcessPaymentRequest.Order, IpayAfricaHelper.OrderTotalSentToIpayAfrica, roundedOrderTotal);
        }
        */
        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.NewPaymentStatus = Core.Domain.Payments.PaymentStatus.Pending;
            return result;
            //return new ProcessPaymentResult();
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //create common query parameters for the request
            var queryParameters = new Dictionary<string, string>();// CreateQueryParameters(postProcessPaymentRequest); //mayank

            //whether to include order items in a transaction
            /*if (_IpayAfricaPaymentSettings.PassProductNamesAndTotals) //mayank
            {
                //add order items query parameters to the request
                var parameters = new Dictionary<string, string>(queryParameters);
                AddItemsParameters(parameters, postProcessPaymentRequest);

                //remove null values from parameters
                parameters = parameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                    .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

                //ensure redirect URL doesn't exceed 2K chars to avoid "too long URL" exception
                var redirectUrl = QueryHelpers.AddQueryString(GetIpayAfricaUrl(), parameters);
                if (redirectUrl.Length <= 2048)
                {
                    _httpContextAccessor.HttpContext.Response.Redirect(redirectUrl);
                    return;
                }
            }*/
            var storeLocation = _webHelper.GetStoreLocation();
            string key = _IpayAfricaPaymentSettings.MerchantKey;
            string autopay = "1";
            string mpesa = "1";
            string airtel = "1";
            string equity = "1";
            string mobile_banking = "0";
            string debit_card = "0";
            string credit_card = "1";
            string mkopo_rahisi = "0";
            string saida = "0";
            var live = "1";
            string order_id = postProcessPaymentRequest.Order.CustomOrderNumber;
            string invoice_number = order_id;
            string callback_url = $"{storeLocation}Plugins/PaymentIpayAfrica/Return";

            string customer_email = postProcessPaymentRequest.Order.ShippingAddress?.Email;
            string mobile_number = postProcessPaymentRequest.Order.ShippingAddress?.PhoneNumber;
            if (mobile_number.Length > 10)
            {
                mobile_number = mobile_number.Remove(0, 4).Insert(0, "0");
            }
            string currency = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId)?.CurrencyCode;
            string vendor_id = _IpayAfricaPaymentSettings.MerchantId;
            var email_notify = "1";
            var curl = "0";
            string cart_total = postProcessPaymentRequest.Order.OrderTotal.ToString();
            string p2 = callback_url;
            string p1 = postProcessPaymentRequest.Order.ShippingAddress?.Email;
            string p3 = currency;
            string p4 = curl;
            string datastring = live.ToString() + order_id + invoice_number + cart_total + mobile_number + customer_email + vendor_id + currency + p1 + p2 + p3 + p4 + callback_url + email_notify.ToString() + curl.ToString();
            byte[] keyByte = new ASCIIEncoding().GetBytes(key);
            byte[] messageBytes = new ASCIIEncoding().GetBytes(datastring);
            byte[] hashmessage = new HMACSHA1(keyByte).ComputeHash(messageBytes);
            String.Concat(Array.ConvertAll(hashmessage, x => x.ToString("x2")));

            //or add only an order total query parameters to the request
            queryParameters.Add("live", live);
            queryParameters.Add("mpesa", mpesa);
            queryParameters.Add("airtel", airtel);
            queryParameters.Add("equity", equity);
            queryParameters.Add("mobilebanking", mobile_banking);
            queryParameters.Add("debitcard", debit_card);
            queryParameters.Add("creditcard", credit_card);
            queryParameters.Add("mkoporahisi", mkopo_rahisi);
            queryParameters.Add("saida", saida);
            queryParameters.Add("oid", order_id);
            queryParameters.Add("inv", invoice_number);
            queryParameters.Add("ttl", cart_total);
            queryParameters.Add("tel", mobile_number);
            queryParameters.Add("eml", customer_email);
            queryParameters.Add("vid", vendor_id);
            queryParameters.Add("curr", currency);
            queryParameters.Add("p1", p1);
            queryParameters.Add("p2", p2);
            queryParameters.Add("p3", p3);
            queryParameters.Add("p4", p4);
            if (_IpayAfricaPaymentSettings.UseDefaultCallBack)
            {
                queryParameters.Add("cbk", _webHelper.GetStoreLocation(false) + "Plugins/PaymentIpayAfrica/Return");
            }
            else
            {
                queryParameters.Add("cbk", _IpayAfricaPaymentSettings.CallBackUrl.Trim());
            }
            queryParameters.Add("cst", email_notify);
            queryParameters.Add("crl", curl);

            queryParameters.Add("hsh", String.Concat(Array.ConvertAll(hashmessage, x => x.ToString("x2"))));

            //AddOrderTotalParameters(queryParameters, postProcessPaymentRequest);

            //remove null values from parameters
            queryParameters = queryParameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

            var url = QueryHelpers.AddQueryString(GetIpayAfricaUrl(), queryParameters);
            _httpContextAccessor.HttpContext.Response.Redirect(url);
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public bool HidePaymentMethod(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return false;
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>Additional handling fee</returns>
        public decimal GetAdditionalHandlingFee(IList<ShoppingCartItem> cart)
        {
            return _paymentService.CalculateAdditionalFee(cart,
               _IpayAfricaPaymentSettings.AdditionalFee, _IpayAfricaPaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public CapturePaymentResult Capture(CapturePaymentRequest capturePaymentRequest)
        {
            return new CapturePaymentResult { Errors = new[] { "Capture method not supported" } };
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        {
            return new RefundPaymentResult { Errors = new[] { "Refund method not supported" } };
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public VoidPaymentResult Void(VoidPaymentRequest voidPaymentRequest)
        {
            return new VoidPaymentResult { Errors = new[] { "Void method not supported" } };
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessRecurringPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public CancelRecurringPaymentResult CancelRecurringPayment(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } };
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public bool CanRePostProcessPayment(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return false;

            return true;
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>List of validating errors</returns>
        public IList<string> ValidatePaymentForm(IFormCollection form)
        {
            return new List<string>();
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>Payment info holder</returns>
        public ProcessPaymentRequest GetPaymentInfo(IFormCollection form)
        {
            var paymentInfo = new ProcessPaymentRequest();
            return paymentInfo;
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentIpayAfrica/Configure";
        }

        /// <summary>
        /// Gets a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <param name="viewComponentName">View component name</param>

        public string GetPublicViewComponentName()
        {
            return "PaymentIpayAfrica";
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        public override void Install()
        {
            //settings
            var settings = new IpayAfricaPaymentSettings() //mayank
            {
                MerchantId = "",
                MerchantKey = "",
                Website = "",
                IndustryTypeId = "",
                PaymentUrl = "",
                CallBackUrl = _webHelper.GetStoreLocation(false) + "Plugins/PaymentIpayAfrica/Return",
                TxnStatusUrl = "",
                UseDefaultCallBack = true
            };
            _settingService.SaveSetting(settings);

            //locales
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.RedirectionTip", "Pay with your MPESA or Airtel Money Mobile Wallet. You will be redirected to IpayAfrica site to complete the order.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.MerchantId", "Merchant ID");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.MerchantId.Hint", "Enter merchant ID.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.UseDefaultCallBack", "Default CallBack");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.UseDefaultCallBack.Hint", "Uncheck and use customized CallBack Url in below field.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.MerchantKey", "Merchant Key");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.MerchantKey.Hint", "Enter Merchant key.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Website", "Website");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Website.Hint", "Enter website param.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.IndustryTypeId", "Industry Type Id");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.IndustryTypeId.Hint", "Enter Industry Type Id.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.PaymentUrl", "Payment URL");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.PaymentUrl.Hint", "Select payment url.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.CallBackUrl", "Callback URL");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.CallBackUrl.Hint", "Enter call back url.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.TxnStatusUrl", "TxnStatus URL");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.TxnStatusUrl.Hint", "Enter TxnStatus back url.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.PaymentMethodDescription", "Pay by IpayAfrica Wallet / credit / debit card / Net Banking");
            base.Install();

        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<IpayAfricaPaymentSettings>();

            //locales
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.RedirectionTip");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.MerchantId");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.MerchantId.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.UseDefaultCallBack");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.UseDefaultCallBack.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.MerchantKey");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.MerchantKey.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Website");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Website.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.IndustryTypeId");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.IndustryTypeId.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.PaymentUrl");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.PaymentUrl.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.CallBackUrl");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.CallBackUrl.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.TxnStatusUrl");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.TxnStatusUrl.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.PaymentMethodDescription");

            base.Uninstall();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get { return PaymentMethodType.Redirection; }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public string PaymentMethodDescription
        {
            //return description of this payment method to be display on "payment method" checkout step. good practice is to make it localizable
            //for example, for a redirection payment method, description may be like this: "You will be redirected to IpayAfrica site to complete the payment"
            get { return _localizationService.GetResource("Plugins.Payments.IpayAfrica.PaymentMethodDescription"); }
        }

        #endregion
    }
}
