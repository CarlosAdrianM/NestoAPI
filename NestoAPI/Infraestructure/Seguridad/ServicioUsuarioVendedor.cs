using NestoAPI.Models;
using System.Linq;

namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// Implementación del servicio para obtener vendedor de usuario.
    /// </summary>
    public class ServicioUsuarioVendedor : IServicioUsuarioVendedor
    {
        public string ObtenerVendedorDeUsuario(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                return null;
            }

            using (var db = new NVEntities())
            {
                var usuarioVendedor = db.UsuarioVendedores
                    .FirstOrDefault(uv => uv.Usuario == userName);

                return usuarioVendedor?.Vendedor?.Trim();
            }
        }
    }
}
