namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Interface para leer parámetros de usuario, permitiendo inyección de dependencias y testing
    /// </summary>
    public interface ILectorParametrosUsuario
    {
        /// <summary>
        /// Lee un parámetro del usuario especificado
        /// </summary>
        /// <param name="empresa">Empresa del usuario</param>
        /// <param name="usuario">Usuario (o "(defecto)" para valores por defecto)</param>
        /// <param name="parametro">Nombre del parámetro</param>
        /// <returns>Valor del parámetro o null si no existe</returns>
        string LeerParametro(string empresa, string usuario, string parametro);
    }
}
