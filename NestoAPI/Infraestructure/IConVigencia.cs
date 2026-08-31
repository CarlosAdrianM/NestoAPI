using System;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Una fila que puede caducar sola. La implementan las dos tablas que tienen fechas de
    /// vigencia con la MISMA semántica:
    ///
    ///   - <c>DescuentosProducto</c> (#423): las campañas de descuento.
    ///   - <c>OfertasPermitidas</c>: las ofertas tipo "6+2".
    ///
    /// Existe para que la regla de "¿está vigente?" viva en un solo sitio
    /// (<see cref="Vigencia"/>) y no acabe habiendo dos, que es como se llega a que una tabla
    /// cuente el último día y la otra no.
    ///
    /// Las entidades son clases parciales generadas del EDMX: la interfaz se les declara en un
    /// fichero aparte, sin tocar el código generado, que se regenera solo.
    /// </summary>
    internal interface IConVigencia
    {
        /// <summary>Nulo = sin límite por ese lado. Inclusiva.</summary>
        DateTime? FechaDesde { get; }

        /// <summary>Nulo = sin límite por ese lado. Inclusiva: vale TODO el día que marca.</summary>
        DateTime? FechaHasta { get; }
    }
}
