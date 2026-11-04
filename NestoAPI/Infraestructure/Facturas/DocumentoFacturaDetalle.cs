using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// NestoAPI#806: «Crear factura» del detalle del pedido (api/Facturas/CrearFactura, servicio.CrearFacturaVenta de Nesto) no pasa
    /// por GestorFacturacionRutas, así que no sabía si la factura iba en papel. Aquí se decide con LA MISMA regla
    /// (<see cref="ReglaImpresionDocumentos"/>), se genera el PDF como en Facturar rutas (papel con membrete), se devuelve en
    /// DatosImpresion y, con el interruptor, se imprime en el servidor (<see cref="IImpresionDocumentosServidor"/>). Nunca lanza: la
    /// factura ya está hecha; un fallo es un aviso más de la respuesta.
    /// </summary>
    public class DocumentoFacturaDetalle
    {
        private readonly IGestorFacturas gestor;
        private readonly IImpresionDocumentosServidor impresion;
        private readonly Func<string, int, Task<CabPedidoVta>> leerPedido;
        private readonly Action<Exception> registrar;

        public DocumentoFacturaDetalle(IGestorFacturas gestor)
            : this(gestor, new ImpresionDocumentosServidor(), LeerPedidoDeLaBd, ElmahHelper.Log)
        {
        }

        internal DocumentoFacturaDetalle(IGestorFacturas gestor, IImpresionDocumentosServidor impresion,
            Func<string, int, Task<CabPedidoVta>> leerPedido, Action<Exception> registrar = null)
        {
            this.gestor = gestor ?? throw new ArgumentNullException(nameof(gestor));
            this.impresion = impresion ?? throw new ArgumentNullException(nameof(impresion));
            this.leerPedido = leerPedido ?? throw new ArgumentNullException(nameof(leerPedido));
            this.registrar = registrar ?? (_ => { });
        }

        /// <param name="factura">La respuesta de CrearFactura (la empresa es la de la factura: si se traspasó, la espejo).</param>
        /// <param name="usuario">El usuario de la petición (el del PDF y el de la impresora de documentos).</param>
        public async Task Completar(CrearFacturaResponseDTO factura, string usuario)
        {
            if (factura == null || string.IsNullOrWhiteSpace(factura.NumeroFactura) || factura.NumeroPedido <= 0)
            {
                return;
            }
            string descripcion = $"la factura {factura.NumeroFactura.Trim()}";
            try
            {
                string empresa = string.IsNullOrWhiteSpace(factura.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : factura.Empresa.Trim();
                CabPedidoVta pedido = await leerPedido(empresa, factura.NumeroPedido).ConfigureAwait(false);
                int copias = ReglaImpresionDocumentos.NumeroCopias(pedido);
                if (copias <= 0)
                {
                    return;
                }

                Factura documento = gestor.LeerFactura(empresa, factura.NumeroFactura.Trim());
                byte[] pdf = await gestor.FacturasEnPDF(new List<Factura> { documento }, papelConMembrete: true, usuario: usuario)
                    .ReadAsByteArrayAsync().ConfigureAwait(false);
                factura.DatosImpresion = new DocumentoParaImprimir
                {
                    BytesPDF = pdf,
                    NumeroCopias = copias,
                    TipoBandeja = ReglaImpresionDocumentos.Bandeja(pedido, esFactura: true)
                };
            }
            catch (Exception ex)
            {
                registrar(new ImpresionDocumentoServidorInfo($"[NestoAPI#806] No se ha podido preparar el PDF de {descripcion} (pedido {factura.NumeroPedido}): {ex.Message}", ex));
                AñadirAviso(factura, $"La factura {factura.NumeroFactura.Trim()} va en papel, pero no se ha podido generar el documento para imprimirla: {ex.Message?.Trim()}");
                return;
            }

            List<string> avisos = impresion.Imprimir(new[]
            {
                new DocumentoAImprimirEnServidor { Descripcion = descripcion, Datos = factura.DatosImpresion }
            }, usuario);
            foreach (string aviso in avisos)
            {
                AñadirAviso(factura, aviso);
            }
        }

        private static void AñadirAviso(CrearFacturaResponseDTO factura, string aviso)
        {
            if (factura.Avisos == null)
            {
                factura.Avisos = new List<string>();
            }
            factura.Avisos.Add(aviso);
        }

        private static async Task<CabPedidoVta> LeerPedidoDeLaBd(string empresa, int numero)
        {
            using (var db = new NVEntities())
            {
                return await db.CabPedidoVtas.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Empresa == empresa && p.Número == numero).ConfigureAwait(false);
            }
        }
    }
}
