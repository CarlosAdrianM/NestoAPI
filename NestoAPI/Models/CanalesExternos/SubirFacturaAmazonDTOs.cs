using System;
using System.Collections.Generic;

namespace NestoAPI.Models.CanalesExternos
{
    /// <summary>NestoAPI#366: petición de POST api/CanalesExternos/Amazon/SubirFactura.</summary>
    public class SubirFacturaAmazonRequestDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
    }

    /// <summary>NestoAPI#366: resultado de facturar (si hacía falta) y subir la factura a Amazon.</summary>
    public class SubirFacturaAmazonResponseDTO
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public string NumeroFactura { get; set; }
        public string AmazonOrderId { get; set; }
        public string MarketplaceId { get; set; }
        public string FeedId { get; set; }
        public string Estado { get; set; }

        /// <summary>Avisos operativos (p. ej. los de la creación de la factura, #327).</summary>
        public List<string> Avisos { get; set; } = new List<string>();
    }

    /// <summary>NestoAPI#366: estado de subida por pedido, para pintar el grid de Nesto al cargar.</summary>
    public class FacturaSubidaAmazonDTO
    {
        public int Pedido { get; set; }
        public string NumeroFactura { get; set; }
        public string Estado { get; set; }
        public DateTime FechaEnvio { get; set; }
    }
}
