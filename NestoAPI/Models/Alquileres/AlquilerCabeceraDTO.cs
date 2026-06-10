using System;
using System.Collections.Generic;

namespace NestoAPI.Models.Alquileres
{
    /// <summary>
    /// Cabecera de alquiler (tabla CabAlquileres) para el grid principal de Alquileres.
    /// Nesto#340 Fase 1C.3: sustituye el acceso EF directo del cliente Nesto. Los campos con
    /// sufijo "Cliente" son de solo lectura (para imprimir la etiqueta del pedido) y se ignoran
    /// al guardar. Numero == 0 indica un alta (la BD asigna el Número con identity).
    /// </summary>
    public class AlquilerCabeceraDTO
    {
        // Clave / identidad
        public string Empresa { get; set; }
        public int Numero { get; set; }

        // Campos editables
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Producto { get; set; }
        public string Inmovilizado { get; set; }
        public int? Cuotas { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public DateTime? FechaSenal { get; set; }
        public decimal? ImporteSenal { get; set; }
        public string NumeroSerie { get; set; }
        public bool? SenalComisiona { get; set; }
        public decimal? Indemnizacion { get; set; }
        public decimal? Importe { get; set; }
        public int? CabPedidoVta { get; set; }
        public string RutaContrato { get; set; }
        public string Comentarios { get; set; }

        // Solo lectura (display / etiquetas)
        public string NombreProducto { get; set; }
        public string Familia { get; set; }
        public string NombreCliente { get; set; }
        public string DireccionCliente { get; set; }
        public string CodPostalCliente { get; set; }
        public string PoblacionCliente { get; set; }
        public string ProvinciaCliente { get; set; }
    }

    /// <summary>
    /// Petición de guardado del grid de alquileres de un producto. El servidor reconcilia
    /// (altas/ediciones/bajas) las cabeceras del producto indicado con la lista recibida.
    /// </summary>
    public class GuardarCabecerasAlquilerRequest
    {
        public string Empresa { get; set; }
        public string Producto { get; set; }
        public List<AlquilerCabeceraDTO> Cabeceras { get; set; }
    }
}
