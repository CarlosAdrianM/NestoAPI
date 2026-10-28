using System;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727: orquestador del fin de mes. Pide a la estrategia los grupos por cliente, reúne los albaranes con el
    /// motor, lo deja en Modificaciones y factura el pedido destino por <c>ServicioFacturas.CrearFactura</c> (único call
    /// site de prdCrearFacturaVta).
    /// </summary>
    public interface IServicioFacturarFinDeMes
    {
        Task<ResultadoFacturacionFinDeMes> Facturar(string empresa, DateTime fechaCorte, string cliente, string contacto, string usuario);
    }
}
