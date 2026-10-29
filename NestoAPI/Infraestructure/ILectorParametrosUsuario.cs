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

        /// <summary>
        /// NestoAPI#739: el parámetro SOLO si el usuario tiene su propia fila (sin caer en «(defecto)» ni crearle la fila).
        /// Para valores personales como la caja (CajaDefecto), donde el valor por defecto de otro no vale.
        /// </summary>
        /// <param name="usuario">Usuario, con o sin dominio (se quita)</param>
        /// <returns>Valor del parámetro (Trim) o null si el usuario no tiene fila propia o está vacía</returns>
        string LeerParametroSinDefecto(string empresa, string usuario, string parametro);
    }
}
