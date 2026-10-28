using System;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727: resultado de facturar el fin de mes. Cada cliente se procesa aislado: un error en uno no para a los demás.
    /// </summary>
    public class ResultadoFacturacionFinDeMes
    {
        public string Empresa { get; set; }
        public DateTime FechaCorte { get; set; }
        public IList<FacturaFinDeMes> Facturas { get; } = new List<FacturaFinDeMes>();
        /// <summary>Clientes que no se han tocado y por qué (no hay nada que mover, P.O. obligatorio, etc.).</summary>
        public IList<ClienteFinDeMesSaltado> Saltados { get; } = new List<ClienteFinDeMesSaltado>();
        public IList<ErrorFinDeMes> Errores { get; } = new List<ErrorFinDeMes>();
    }

    public class FacturaFinDeMes
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public int PedidoDestino { get; set; }
        public IList<int> PedidosReunidos { get; set; } = new List<int>();
        public int LineasMovidas { get; set; }
        public string NumeroFactura { get; set; }
        /// <summary>Algo que revisar aunque la factura esté hecha (o null).</summary>
        public string Aviso { get; set; }
    }

    public class ClienteFinDeMesSaltado
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public IList<int> Pedidos { get; set; } = new List<int>();
        public string Motivo { get; set; }
    }

    public class ErrorFinDeMes
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        /// <summary>El pedido en el que ya se han reunido los albaranes (si el error es al facturar), o null.</summary>
        public int? PedidoDestino { get; set; }
        public string Mensaje { get; set; }
    }
}
