using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security.DataProtection;
using NestoAPI.Infraestructure;
using NestoAPI.Models;
using System;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;

namespace NestoAPI.Controllers
{
    [EnableCors("*", "*", "*")]
    [RoutePrefix("api/accounts")]
    public class AccountsController : BaseApiController
    {
        [Authorize(Roles = "Admin")]
        [Route("users")]
        public IHttpActionResult GetUsers()
        {
            return Ok(AppUserManager.Users.ToList().Select(u => TheModelFactory.Create(u)));
        }

        [Authorize(Roles = "Admin")]
        [Route("user/{id:guid}", Name = "GetUserById")]
        public async Task<IHttpActionResult> GetUser(string Id)
        {
            ApplicationUser user = await AppUserManager.FindByIdAsync(Id);

            return user != null ? Ok(TheModelFactory.Create(user)) : (IHttpActionResult)NotFound();
        }

        [Authorize(Roles = "Admin")]
        [Route("user/{username}")]
        public async Task<IHttpActionResult> GetUserByName(string username)
        {
            ApplicationUser user = await AppUserManager.FindByNameAsync(username);

            return user != null ? Ok(TheModelFactory.Create(user)) : (IHttpActionResult)NotFound();
        }

        [AllowAnonymous]
        [Route("create")]
        public async Task<IHttpActionResult> CreateUser(CreateUserBindingModel createUserModel)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            ApplicationUser user = new ApplicationUser()
            {
                UserName = createUserModel.Username,
                Email = createUserModel.Email,
                FirstName = createUserModel.FirstName,
                LastName = createUserModel.LastName,
                Level = 3,
                JoinDate = DateTime.Now.Date,
            };

            IdentityResult addUserResult = await AppUserManager.CreateAsync(user, createUserModel.Password);

            if (!addUserResult.Succeeded)
            {
                return GetErrorResult(addUserResult);
            }

            Uri locationHeader = new Uri(Url.Link("GetUserById", new { id = user.Id }));

            return Created(locationHeader, TheModelFactory.Create(user));
        }

        [Authorize(Roles = "Admin")]
        [Route("user/{id:guid}/roles")]
        [HttpPut]
        public async Task<IHttpActionResult> AssignRolesToUser([FromUri] string id, [FromBody] string[] rolesToAssign)
        {

            ApplicationUser appUser = await AppUserManager.FindByIdAsync(id);

            if (appUser == null)
            {
                return NotFound();
            }

            System.Collections.Generic.IList<string> currentRoles = await AppUserManager.GetRolesAsync(appUser.Id);

            string[] rolesNotExists = rolesToAssign.Except(AppRoleManager.Roles.Select(x => x.Name)).ToArray();

            if (rolesNotExists.Count() > 0)
            {

                ModelState.AddModelError("", string.Format("Roles '{0}' does not exixts in the system", string.Join(",", rolesNotExists)));
                return BadRequest(ModelState);
            }

            IdentityResult removeResult = await AppUserManager.RemoveFromRolesAsync(appUser.Id, currentRoles.ToArray());

            if (!removeResult.Succeeded)
            {
                ModelState.AddModelError("", "Failed to remove user roles");
                return BadRequest(ModelState);
            }

            IdentityResult addResult = await AppUserManager.AddToRolesAsync(appUser.Id, rolesToAssign);

            if (!addResult.Succeeded)
            {
                ModelState.AddModelError("", "Failed to add user roles");
                return BadRequest(ModelState);
            }

            return Ok();
        }

        [HttpPost]
        [Route("OlvideMiContrasenna")]
        public async Task<IHttpActionResult> OlvideMiContrasenna(string correo)
        {
            ApplicationUser user = await AppUserManager.FindByEmailAsync(correo).ConfigureAwait(false);
            if (user == null)
            {
                return NotFound();
            }
            DpapiDataProtectionProvider provider = new DpapiDataProtectionProvider("SampleAppNameCarlos");
            AppUserManager.UserTokenProvider = new DataProtectorTokenProvider<ApplicationUser>(provider.Create("SampleTokenNameCarlos"));
            string token = await AppUserManager.GeneratePasswordResetTokenAsync(user.Id).ConfigureAwait(false);
            try
            {


                //string passwordResetLink = Url.Link("DefaultApi", new { Controller = "Home", Action = "ResetPassword", email = correo, token });
                string passwordResetLink = Url.Link("DefaultApi", new { controller = "Home", id = "ResetPassword", email = correo, token })
                               .Replace("/api/", "/");
                MailMessage mail = new MailMessage(new MailAddress("nesto@nuevavision.es"), new MailAddress(correo))
                {
                    Body = $"<a href=\"{passwordResetLink}\">Haz clic aquí para restablecer tu contraseña</a>",
                    Subject = "Recuperación de contraseña NestoApp",
                    IsBodyHtml = true
                };


                SmtpClient client = new SmtpClient
                {
                    Port = 587,
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false
                };
                string contrasenna = ConfigurationManager.AppSettings["office365password"];
                client.Credentials = new System.Net.NetworkCredential("nesto@nuevavision.es", contrasenna);
                client.Host = "smtp.office365.com";

                client.Send(mail);

                return Ok();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }
}
