using NestoAPI.Controllers;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Implementación que delega en ParametrosUsuarioController.LeerParametro
    /// </summary>
    public class LectorParametrosUsuario : ILectorParametrosUsuario
    {
        public string LeerParametro(string empresa, string usuario, string parametro)
        {
            return ParametrosUsuarioController.LeerParametro(empresa, usuario, parametro);
        }

        public string LeerParametroSinDefecto(string empresa, string usuario, string parametro)
        {
            return ParametrosUsuarioController.LeerParametroSinDefecto(empresa, usuario, parametro);
        }
    }
}
