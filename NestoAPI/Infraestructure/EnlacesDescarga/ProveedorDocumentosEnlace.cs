using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.EnlacesDescarga
{
    /// <summary>
    /// NestoAPI#737: implementación real (BD + QuestPDF). El PDF del pedido es la proforma de «Descargar PDF» (GestorFacturas.LeerPedido)
    /// y el de la factura, el de GET api/Facturas; los dos con QuestPDF siempre (quien descarga no es un usuario de Nesto y no tiene
    /// ParametrosUsuario, como los clientes de la tienda online, TiendasNuevaVision#15).
    /// </summary>
    public class ProveedorDocumentosEnlace : IProveedorDocumentosEnlace
    {
        public async Task<DatosDocumentoEnlace> LeerDatos(string tipo, string empresa, string numero)
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                string cliente;
                string contacto;
                decimal importe;
                if (tipo == TiposDocumentoDescarga.PEDIDO)
                {
                    int pedido = int.Parse(numero);
                    var cab = await db.CabPedidoVtas
                        .Where(c => c.Empresa == empresa && c.Número == pedido)
                        .Select(c => new { c.Nº_Cliente, c.Contacto })
                        .FirstOrDefaultAsync().ConfigureAwait(false);
                    if (cab == null)
                    {
                        return null;
                    }
                    cliente = cab.Nº_Cliente;
                    contacto = cab.Contacto;
                    importe = await db.LinPedidoVtas
                        .Where(l => l.Empresa == empresa && l.Número == pedido)
                        .Select(l => (decimal?)l.Total).SumAsync().ConfigureAwait(false) ?? 0;
                }
                else
                {
                    ComprobarSeriePermitida(numero);
                    var cab = await db.CabsFacturasVtas
                        .Where(c => c.Empresa == empresa && c.Número == numero)
                        .Select(c => new { c.Nº_Cliente, c.Contacto })
                        .FirstOrDefaultAsync().ConfigureAwait(false);
                    if (cab == null)
                    {
                        return null;
                    }
                    cliente = cab.Nº_Cliente;
                    contacto = cab.Contacto;
                    importe = await db.LinPedidoVtas
                        .Where(l => l.Empresa == empresa && l.Nº_Factura == numero)
                        .Select(l => (decimal?)l.Total).SumAsync().ConfigureAwait(false) ?? 0;
                }

                var ficha = await db.Clientes
                    .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto)
                    .Select(c => new { c.Nombre, c.Teléfono, c.Pais })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                List<string> telefonosContacto = await db.PersonasContactoClientes
                    .Where(p => p.Empresa == empresa && p.NºCliente == cliente && p.Contacto == contacto
                        && p.Estado >= Constantes.Clientes.PersonasContacto.ESTADO_POR_DEFECTO && p.Teléfono != null)
                    .OrderBy(p => p.Número)
                    .Select(p => p.Teléfono)
                    .ToListAsync().ConfigureAwait(false);

                var telefonos = new List<string>();
                if (!string.IsNullOrWhiteSpace(ficha?.Teléfono))
                {
                    telefonos.Add(ficha.Teléfono);
                }
                telefonos.AddRange(telefonosContacto);
                return new DatosDocumentoEnlace
                {
                    Cliente = cliente?.Trim(),
                    Contacto = contacto?.Trim(),
                    Nombre = ficha?.Nombre?.Trim(),
                    Pais = ficha?.Pais?.Trim(),
                    Importe = importe,
                    Telefonos = telefonos
                };
            }
        }

        public async Task<byte[]> Pdf(string tipo, string empresa, string numero)
        {
            var gestor = new GestorFacturas();
            Factura documento;
            if (tipo == TiposDocumentoDescarga.PEDIDO)
            {
                documento = gestor.LeerPedido(empresa, int.Parse(numero));
            }
            else
            {
                ComprobarSeriePermitida(numero);
                documento = gestor.LeerFactura(empresa, numero);
            }
            if (documento == null)
            {
                return null;
            }
            return await gestor.FacturasEnPDF(new List<Factura> { documento }, papelConMembrete: false, usuario: null,
                mostrarImagenes: false, forzarQuestPdf: true).ReadAsByteArrayAsync().ConfigureAwait(false);
        }

        /// <summary>Como GET api/Facturas: las series sin correo de facturas (CorreoDesdeFactura null) no se descargan.</summary>
        private static void ComprobarSeriePermitida(string factura)
        {
            if (factura == null || factura.Length < 2)
            {
                return;
            }
            ISerieFactura serie;
            try
            {
                serie = GestorFacturas.LeerSerie(factura.Substring(0, 2));
            }
            catch (Exception)
            {
                // Serie desconocida: dará «no existe» después
                return;
            }
            if (serie != null && serie.CorreoDesdeFactura == null)
            {
                throw new NestoBusinessException($"Las facturas de la serie {factura.Substring(0, 2)} no se pueden descargar.")
                {
                    StatusCode = HttpStatusCode.Forbidden
                };
            }
        }
    }
}
