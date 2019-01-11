using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Plugins;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Tax;

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
        private readonly ISettingService _settingService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly IpayAfricaPaymentSettings _IpayAfricaPaymentSettings;
        //private readonly PaymentIpayAfricaController _PaymentIpayAfricaController;

        #endregion

        #region Ctor

        public IpayAfricaPaymentProcessor(CurrencySettings currencySettings,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICurrencyService currencyService,
            IGenericAttributeService genericAttributeService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IPaymentService paymentService,
            ISettingService settingService,
            ITaxService taxService,
            IWebHelper webHelper,
            //PaymentIpayAfricaController paymentIpayAfricaController,
            IpayAfricaPaymentSettings IpayAfricaPaymentSettings)
        {
            this._currencySettings = currencySettings;
            this._checkoutAttributeParser = checkoutAttributeParser;
            this._currencyService = currencyService;
            this._genericAttributeService = genericAttributeService;
            this._httpContextAccessor = httpContextAccessor;
            this._localizationService = localizationService;
            this._paymentService = paymentService;
            this._settingService = settingService;
            this._taxService = taxService;
            this._webHelper = webHelper;
            //this._PaymentIpayAfricaController = paymentIpayAfricaController;
            this._IpayAfricaPaymentSettings = IpayAfricaPaymentSettings;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Gets Ipay URL
        /// </summary>
        /// <returns></returns>
        private string GetIpayUrl()
        {
            return _IpayAfricaPaymentSettings.Live ?
                "https://payments.ipayafrica.com/v3/ke" :
                "https://payments.ipayafrica.com/v3/ke";
        }

        /// <summary>
        /// Gets IPN Ipay URL
        /// </summary>
        /// <returns></returns>
        private string GetIpnIpayUrl()
        {
            return _IpayAfricaPaymentSettings.Live ?
                "https://www.ipayafrica.com/ipn/vendor" :
                "https://www.ipayafrica.com/ipn/vendor" /*+ _IpayAfricaPaymentSettings.VendorID + id*/;
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
            var req = (HttpWebRequest)WebRequest.Create(GetIpayUrl());
            req.Method = WebRequestMethods.Http.Post;
            req.ContentType = MimeTypes.ApplicationXWwwFormUrlencoded;
            //now Ipay requires user-agent. otherwise, we can get 403 error
            //req.UserAgent = _IpayAfricaPaymentSettings.VendorID;
            req.UserAgent = _httpContextAccessor.HttpContext.Request.Headers[HeaderNames.UserAgent];

            var formContent = $"cmd=_notify-synch&at={_IpayAfricaPaymentSettings.HashKey}&tx={tx}";
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
                    success = line.Equals("aei7p7yrx4ae34", StringComparison.OrdinalIgnoreCase);
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
            var req = (HttpWebRequest)WebRequest.Create(GetIpnIpayUrl());
            req.Method = WebRequestMethods.Http.Post;
            req.ContentType = MimeTypes.ApplicationXWwwFormUrlencoded;
            //now Ipay requires user-agent. otherwise, we can get 403 error
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
            var success = response.Trim().Equals("aei7p7yrx4ae34", StringComparison.OrdinalIgnoreCase);

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
        private IDictionary<string, string> CreateQueryParameters(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //get store location
            var storeLocation = _webHelper.GetStoreLocation();
            string key = _IpayAfricaPaymentSettings.HashKey;
            string autopay = "1";
            string p1 = "";
            string p2 = "";
            string p3 = "";
            string p4 = "";
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
            string callback_url = $"{storeLocation}orderdetails/{order_id}";
            string customer_email = postProcessPaymentRequest.Order.ShippingAddress?.Email;
            string mobile_number = postProcessPaymentRequest.Order.ShippingAddress?.PhoneNumber;
            string currency = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId)?.CurrencyCode;
            string vendor_id = _IpayAfricaPaymentSettings.VendorID;
            var email_notify = "1";
            var curl = "0";
            string cart_total = postProcessPaymentRequest.Order.OrderTotal.ToString();
            string datastring = live.ToString() + order_id + invoice_number + cart_total + mobile_number + customer_email + vendor_id + currency + p1 + p2 + p3 + p4 + callback_url + email_notify.ToString() + curl.ToString();
            byte[] keyByte = new ASCIIEncoding().GetBytes(key);
            byte[] messageBytes = new ASCIIEncoding().GetBytes(datastring);
            byte[] hashmessage = new HMACSHA1(keyByte).ComputeHash(messageBytes);
            
            // to lowercase hexits
            String.Concat(Array.ConvertAll(hashmessage, x => x.ToString("x2")));

            // to base64
            //Convert.ToBase64String(hashmessage);
            //create query parameters
            return new Dictionary<string, string>
            {
                ["live"] = live,
                ["mpesa"] = mpesa,
                ["airtel"] = airtel,
                ["equity"] = equity,
                ["mobilebanking"] = mobile_banking,
                ["debitcard"] = debit_card,
                ["creditcard"] = credit_card,
                ["mkoporahisi"] = mkopo_rahisi,
                ["saida"] = saida,
                ["oid"] = order_id,
                ["inv"] = invoice_number,
                ["ttl"] = cart_total,
                ["tel"] = mobile_number,
                ["eml"] = customer_email,
                ["vid"] = vendor_id,
                ["curr"] = currency,
                ["p1"] = p1,
                ["p2"] = p2,
                ["p3"] = p3,
                ["p4"] = p4,
                ["cbk"] = callback_url,
                ["cst"] = email_notify,
                ["crl"] = curl,
                ["hsh"] = String.Concat(Array.ConvertAll(hashmessage, x => x.ToString("x2")))
            };
        }

        /// <summary>
        /// Add order items to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        private void AddItemsParameters(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
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

                //gift card or rewarded point amount applied to cart in nopCommerce - shows in Ipay as "discount"
                parameters.Add("discount_amount_cart", discountTotal.ToString("0.00", CultureInfo.InvariantCulture));
            }

            //save order total that actually sent to Ipay (used for PDT order total validation)
            _genericAttributeService.SaveAttribute(postProcessPaymentRequest.Order, IpayAfricaHelper.OrderTotalSentToIpay, roundedCartTotal);
        }

        /// <summary>
        /// Add order total to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        private void AddOrderTotalParameters(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //round order total
            var roundedOrderTotal = Math.Round(postProcessPaymentRequest.Order.OrderTotal, 2);

            parameters.Add("cmd", "_xclick");
            parameters.Add("item_name", $"Order Number {postProcessPaymentRequest.Order.CustomOrderNumber}");
            parameters.Add("amount", roundedOrderTotal.ToString("0.00", CultureInfo.InvariantCulture));

            //save order total that actually sent to Ipay (used for PDT order total validation)
            _genericAttributeService.SaveAttribute(postProcessPaymentRequest.Order, IpayAfricaHelper.OrderTotalSentToIpay, roundedOrderTotal);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public ProcessPaymentResult ProcessPayment(ProcessPaymentRequest processPaymentRequest)
        {
            return new ProcessPaymentResult();
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //create common query parameters for the request
            var queryParameters = CreateQueryParameters(postProcessPaymentRequest);

            //whether to include order items in a transaction
            if (_IpayAfricaPaymentSettings.PassProductNamesAndTotals)
            {
                //add order items query parameters to the request
                var parameters = new Dictionary<string, string>(queryParameters);
                AddItemsParameters(parameters, postProcessPaymentRequest);

                //remove null values from parameters
                parameters = parameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                    .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

                //ensure redirect URL doesn't exceed 2K chars to avoid "too long URL" exception
                var redirectUrl = QueryHelpers.AddQueryString(GetIpayUrl(), parameters);
                if (redirectUrl.Length <= 2048)
                {
                    _httpContextAccessor.HttpContext.Response.Redirect(redirectUrl);
                    return;
                }
            }

            //or add only an order total query parameters to the request
            AddOrderTotalParameters(queryParameters, postProcessPaymentRequest);

            //remove null values from parameters
            queryParameters = queryParameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);

            var url = QueryHelpers.AddQueryString(GetIpayUrl(), queryParameters);
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
            return new ProcessPaymentRequest();
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentIpayAfrica/Configure";
        }

        /// <summary>
        /// Gets a name of a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>View component name</returns>
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
            _settingService.SaveSetting(new IpayAfricaPaymentSettings
            {
                Live = true
            });

            //locales
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.AdditionalFee", "Additional fee");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.AdditionalFeePercentage", "Additional fee. Use percentage");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.AdditionalFeePercentage.Hint", "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.VendorID", "Vendor ID");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.VendorID.Hint", "Specify your Ipay Vendor ID. It shuld be in lowercase.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.PassProductNamesAndTotals", "Pass product names and order totals to Ipay");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.PassProductNamesAndTotals.Hint", "Check if product names and order totals should be passed to Ipay.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.HashKey", "Your Ipay vendor hashkey");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.HashKey.Hint", "Specify your Ipay vendor hashkey in lowercase");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.RedirectionTip", "<p>You will be redirected to Ipay Africa to complete the order using mobile money.</p><p> Once you've received a successful mobile money transaction notification, press the complete button to complete your checkout.</p>");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.Live", "Live");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.Live.Hint", "Check to enable live environment.");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.Instructions", "<p>Allow your Customers to pay with Ipay Africa using mobile money and credit cards. Head to <a href=\"https://ipayafrica.com\" target=\"_blank\">Ipay Africa</a> and sign up for a merchant account to get your vendor ID and key.</p>");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.PaymentMethodDescription", "You will be redirected to Ipay Africa to complete the payment");
            _localizationService.AddOrUpdatePluginLocaleResource("Plugins.Payments.IpayAfrica.RoundingWarning", "It looks like you have \"ShoppingCartSettings.RoundPricesDuringCalculation\" setting disabled. Keep in mind that this can lead to a discrepancy of the order total amount, as Ipay only rounds to two decimals.");

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
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.AdditionalFee");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.AdditionalFee.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.AdditionalFeePercentage");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.AdditionalFeePercentage.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.VendorID");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.VendorID.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.PassProductNamesAndTotals");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.PassProductNamesAndTotals.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.HashKey");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.HashKey.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.RedirectionTip");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.Live");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Fields.Live.Hint");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.Instructions");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.PaymentMethodDescription");
            _localizationService.DeletePluginLocaleResource("Plugins.Payments.IpayAfrica.RoundingWarning");

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
            //for example, for a redirection payment method, description may be like this: "You will be redirected to Ipay site to complete the payment"
            get { return _localizationService.GetResource("Plugins.Payments.IpayAfrica.PaymentMethodDescription"); }
        }

        #endregion
    }
}