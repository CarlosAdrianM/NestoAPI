using NestoAPI.Infraestructure.PedidosVenta;
using System;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#693: un pedido cuya fecha de entrega a la agencia prometida ya no se cumple (GET api/PedidosVenta/PromesaEnRiesgo).
    /// Las fechas son días, sin hora.
    /// </summary>
    public class PedidoPromesaRiesgoDTO
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string NombreCliente { get; set; }
        /// <summary>El de la cabecera (estética).</summary>
        public string Vendedor { get; set; }
        /// <summary>El de peluquería del pedido, si lo tiene.</summary>
        public string VendedorPeluqueria { get; set; }
        public string Ruta { get; set; }
        /// <summary>La que se le dio al cliente al crear el pedido.</summary>
        public DateTime FechaPrometida { get; set; }
        /// <summary>La de ahora. Null = sin fecha.</summary>
        public DateTime? FechaNueva { get; set; }
        public bool SinFecha => !FechaNueva.HasValue;
        /// <summary>Días de retraso (null si está sin fecha).</summary>
        public int? DiasRetraso => FechaNueva.HasValue ? (int?)(FechaNueva.Value.Date - FechaPrometida.Date).TotalDays : null;
        /// <summary>Qué productos fallan y por qué, de usted.</summary>
        public string Motivo { get; set; }
        /// <summary>El evento que lo detectó (o lo empeoró por última vez).</summary>
        public string Evento { get; set; }
        public DateTime Detectado { get; set; }
        public DateTime Actualizado { get; set; }
        public DateTime? AvisadoCampana { get; set; }
        public DateTime? AvisadoCorreo { get; set; }
        /// <summary>Nesto#546: cuándo se resolvió (null = sigue en riesgo). Solo vienen resueltos pidiéndolos (resueltos=true).</summary>
        public DateTime? Resuelto { get; set; }
        /// <summary>Por qué se resolvió (vuelve a cumplir la fecha, se sirvió, se renegoció…).</summary>
        public string MotivoResolucion { get; set; }
        public bool EstaResuelto => Resuelto.HasValue;

        public static PedidoPromesaRiesgoDTO De(RiesgoPromesaListado r) => new PedidoPromesaRiesgoDTO
        {
            Id = r.Id,
            Empresa = r.Empresa?.Trim(),
            Pedido = r.Pedido,
            Cliente = r.Cliente?.Trim(),
            Contacto = r.Contacto?.Trim(),
            NombreCliente = r.NombreCliente?.Trim(),
            Vendedor = r.Vendedor?.Trim(),
            VendedorPeluqueria = r.VendedorPeluqueria?.Trim(),
            Ruta = r.Ruta?.Trim(),
            FechaPrometida = r.FechaPrometida.Date,
            FechaNueva = r.FechaNueva?.Date,
            Motivo = r.Motivo,
            Evento = r.Evento,
            Detectado = r.Detectado,
            Actualizado = r.Actualizado,
            AvisadoCampana = r.AvisadoCampana,
            AvisadoCorreo = r.AvisadoCorreo,
            Resuelto = r.Resuelto,
            MotivoResolucion = r.MotivoResolucion
        };
    }
}
