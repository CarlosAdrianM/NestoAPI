using System.ComponentModel.DataAnnotations;

namespace NestoAPI.ViewModels
{
    public class ResetPasswordViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
        [DataType(DataType.Password)]
        [Display(Name ="Confirmar contraseña")]
        [Compare("Password",ErrorMessage ="Las contraseñas no coinciden")]
        public string ConfirmPassword { get; set; }
        public string Token { get; set; }
    }
}