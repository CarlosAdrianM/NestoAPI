using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Cobros
{
    /// <summary>
    /// NestoAPI#534: un efecto de cartera vencido por transferencia candidato al aviso al cliente.
    /// Con <see cref="Motivo"/> a null se le avisaría; con motivo, se queda fuera y el motivo dice
    /// por qué (en el modo sombra se enseñan los dos grupos para que administración valide el criterio).
    /// NestoAPI#544: lleva además la memoria del efecto (qué número de aviso sería y cuándo toca).
    /// </summary>
    public class AvisoFacturaVencidaDTO
    {
        /// <summary>Nº de orden del efecto en ExtractoCliente (la clave del efecto).</summary>
        public int NOrden { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Factura { get; set; }
        public string Efecto { get; set; }
        public DateTime? FechaFactura { get; set; }
        public DateTime Vencimiento { get; set; }
        /// <summary>Importe PENDIENTE del efecto (lo que se reclama), no el total de la factura.</summary>
        public decimal Importe { get; set; }
        public int DiasVencida { get; set; }
        /// <summary>Correos a los que iría el aviso, separados por coma. Vacío si la ficha no tiene.</summary>
        public string Destinatarios { get; set; }
        /// <summary>
        /// NestoAPI#544 (d): nombre de la persona de contacto a la que se escribe (Saludo o Nombre de
        /// la persona de Cobros; si no, de la de Factura por correo). Null si no hay: se saluda por la
        /// razón social.
        /// </summary>
        public string NombrePersonaContacto { get; set; }
        public string Motivo { get; set; }
        public bool SeAvisaria => Motivo == null;

        /// <summary>NestoAPI#544 (c): número de aviso que sería el de hoy (1 si nunca se ha avisado o si el reloj se ha reiniciado).</summary>
        public int NumeroAviso { get; set; } = 1;
        /// <summary>Fecha del último aviso registrado, si lo hay.</summary>
        public DateTime? FechaUltimoAviso { get; set; }
        /// <summary>Número del último aviso registrado (0 si ninguno).</summary>
        public int NumeroUltimoAviso { get; set; }
        /// <summary>Día en que toca (o tocaba) el aviso número <see cref="NumeroAviso"/> según la cadencia. Null si nunca se ha avisado.</summary>
        public DateTime? FechaSiguienteAviso { get; set; }
        /// <summary>
        /// Si hoy toca avisar de ESTE efecto según la cadencia. Un efecto avisable que no toca hoy
        /// puede ir igualmente en el correo del cliente si a otro efecto suyo sí le toca.
        /// </summary>
        public bool TocaHoy { get; set; } = true;
        /// <summary>Si el pendiente de hoy es menor que el del último aviso: el cliente ha pagado parte y la cuenta empieza de nuevo.</summary>
        public bool ReinicioPorPagoParcial { get; set; }
    }

    /// <summary>
    /// NestoAPI#544 (e): el aviso que recibe un cliente, con TODAS sus facturas vencidas avisables.
    /// </summary>
    public class AvisoClienteFacturasVencidasDTO
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Destinatarios { get; set; }
        public string NombrePersonaContacto { get; set; }
        public List<AvisoFacturaVencidaDTO> Facturas { get; set; } = new List<AvisoFacturaVencidaDTO>();
        /// <summary>El número de aviso del correo es el del efecto más antiguo del cliente (Carlos, 25/09/26).</summary>
        public int NumeroAviso => Facturas.OrderBy(f => f.Vencimiento).ThenBy(f => f.NOrden).FirstOrDefault()?.NumeroAviso ?? 1;
        public decimal Total => Facturas.Sum(f => f.Importe);
        public List<string> NumerosFactura => Facturas.Select(f => f.Factura).Where(f => !string.IsNullOrEmpty(f)).Distinct().ToList();
        /// <summary>Saludo ya resuelto ("Buenos días, Susana:"); lo pone el job. Null = la plantilla pone el genérico.</summary>
        public string Saludo { get; set; }
    }

    /// <summary>NestoAPI#544 (e): datos para el bloque de «copiar y pegar» del aviso.</summary>
    public class DatosPagoAviso
    {
        /// <summary>IBAN formateado (de la tabla Bancos, vía ServicioFacturas.CuentaBancoEmpresa). Null si no hay.</summary>
        public string Iban { get; set; }
        /// <summary>Titular de la cuenta (Empresas.Nombre).</summary>
        public string Titular { get; set; }
    }
}
