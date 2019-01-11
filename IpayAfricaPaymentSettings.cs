using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.IpayAfrica
{
    /// <summary>
    /// Represents settings of the Ipay Africa payment plugin
    /// </summary>
    public class IpayAfricaPaymentSettings : ISettings
    {
        /// <summary>
        /// Gets or sets a value indicating whether to use sandbox (testing environment)
        /// </summary>
        public bool Live { get; set; }

        /// <summary>
        /// Gets or sets a business email
        /// </summary>
        public string VendorID { get; set; }

        /// <summary>
        /// Gets or sets PDT identity token
        /// </summary>
        public string HashKey { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to pass info about purchased items to Ipay
        /// </summary>
        public bool PassProductNamesAndTotals { get; set; }

        /// <summary>
        /// Gets or sets an additional fee
        /// </summary>
        public decimal AdditionalFee { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to "additional fee" is specified as percentage. true - percentage, false - fixed value.
        /// </summary>
        public bool AdditionalFeePercentage { get; set; }
    }
}
