using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.IpayAfrica.Models
{
    public class ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.IpayAfrica.UseDefaultCallBack")]
        public bool UseDefaultCallBack { get; set; }

        [NopResourceDisplayName("Plugins.Payments.IpayAfrica.MerchantId")]
        public string MerchantId { get; set; }

        [NopResourceDisplayName("Plugins.Payments.IpayAfrica.MerchantKey")] //Encryption Key
        public string MerchantKey { get; set; }

        [NopResourceDisplayName("Plugins.Payments.IpayAfrica.Website")]
        public string Website { get; set; }

        [NopResourceDisplayName("Plugins.Payments.IpayAfrica.IndustryTypeId")]//Payment URI
        public string IndustryTypeId { get; set; }

        [NopResourceDisplayName("Plugins.Payments.IpayAfrica.PaymentUrl")]
        public string PaymentUrl { get; set; }

        [NopResourceDisplayName("Plugins.Payments.IpayAfrica.CallBackUrl")]
        public string CallBackUrl { get; set; }

        [NopResourceDisplayName("Plugins.Payments.IpayAfrica.TxnStatusUrl")]
        public string TxnStatusUrl { get; set; }


    }
}