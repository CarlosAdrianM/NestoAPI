using NestoAPI.Infraestructure;

namespace NestoAPI.Models
{
    /// <summary>
    /// Las dos entidades que pueden caducar solas declaran aquí que cumplen
    /// <see cref="IConVigencia"/>. Va en un fichero aparte a propósito: DescuentosProducto.cs y
    /// OfertaPermitida.cs los genera el EDMX y se sobrescriben en cuanto alguien actualiza el
    /// modelo desde la base de datos.
    ///
    /// No hace falta implementar nada: las propiedades FechaDesde y FechaHasta ya existen en el
    /// código generado, con el tipo que pide la interfaz.
    /// </summary>
    public partial class DescuentosProducto : IConVigencia
    {
    }

    public partial class OfertaPermitida : IConVigencia
    {
    }
}
