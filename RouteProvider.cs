using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.IpayAfrica
{
    public partial class RouteProvider : IRouteProvider
    {
        /// <summary>
        /// Register routes
        /// </summary>
        /// <param name="routeBuilder">Route builder</param>
        public void RegisterRoutes(IRouteBuilder routeBuilder)
        {
            //PDT
            routeBuilder.MapRoute("Plugin.Payments.IpayAfrica.PDTHandler", "Plugins/PaymentIpayAfrica/PDTHandler",
                 new { controller = "PaymentIpayAfrica", action = "PDTHandler" });

            //IPN
            routeBuilder.MapRoute("Plugin.Payments.IpayAfrica.IPNHandler", "Plugins/PaymentIpayAfrica/IPNHandler",
                 new { controller = "PaymentIpayAfrica", action = "IPNHandler" });

            //Cancel
            routeBuilder.MapRoute("Plugin.Payments.IpayAfrica.CancelOrder", "Plugins/PaymentIpayAfrica/CancelOrder",
                 new { controller = "PaymentIpayAfrica", action = "CancelOrder" });
        }

        /// <summary>
        /// Gets a priority of route provider
        /// </summary>
        public int Priority
        {
            get { return -1; }
        }
    }
}
