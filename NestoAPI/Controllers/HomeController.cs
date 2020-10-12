using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security.DataProtection;
using NestoAPI.Infraestructure;
using NestoAPI.ViewModels;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

namespace NestoAPI.Controllers
{
    public class HomeController : Controller
    {
        private ApplicationUserManager _AppUserManager = null;

        protected ApplicationUserManager AppUserManager
        {
            get
            {
                return _AppUserManager ?? Request.GetOwinContext().GetUserManager<ApplicationUserManager>();
            }
        }
        public ActionResult Index()
        {
            ViewBag.Title = "Home Page";

            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public ActionResult ResetPassword(string token, string email)
        {
            if (token == null || email == null)
            {
                ModelState.AddModelError("", "El token no es válido");
            }
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                ApplicationUser user = await AppUserManager.FindByEmailAsync(model.Email).ConfigureAwait(false);

                if (user != null)
                {
                    var provider = new DpapiDataProtectionProvider("SampleAppNameCarlos");
                    AppUserManager.UserTokenProvider = new DataProtectorTokenProvider<ApplicationUser>(provider.Create("SampleTokenNameCarlos"));
                    var result = await AppUserManager.ResetPasswordAsync(user.Id, model.Token, model.Password).ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        return View("ResetPasswordConfirmation");
                    }
                    foreach(var error in result.Errors)
                    {
                        ModelState.AddModelError("", error);
                    }
                    return View(model);
                }
                return View("ResetPasswordConfirmation");
            }
            return View(model);
        }
    }
}
