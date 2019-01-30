using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.IpayAfrica
{
    public class IpayAfricaPaymentSettings : ISettings
    {
        public string MerchantId { get; set; }
        public string MerchantKey { get; set; }
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFeePercentage { get; set; }
    }
}
