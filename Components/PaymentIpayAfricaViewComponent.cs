using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Payments.IpayAfrica.Models;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.IpayAfrica.Components
{
    [ViewComponent(Name = "PaymentIpayAfrica")]
    public class PaymentIpayAfricaViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            var model = new PaymentInfoModel()
            {

            };

            return View("~/Plugins/Payments.IpayAfrica/Views/PaymentInfo.cshtml", model);
        }
    }
}
