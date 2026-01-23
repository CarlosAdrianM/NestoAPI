namespace NestoAPI.Infraestructure.Seguridad
{
    /// <summary>
    /// Servicio para obtener información de vendedor asociado a un usuario.
    /// </summary>
    public interface IServicioUsuarioVendedor
    {
        /// <summary>
        /// Obtiene el código de vendedor asociado a un usuario.
        /// </summary>
        /// <param name="userName">Nombre de usuario</param>
        /// <returns>Código del vendedor o null si no tiene uno asociado</returns>
        string ObtenerVendedorDeUsuario(string userName);
    }
}
