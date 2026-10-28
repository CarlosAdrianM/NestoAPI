using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.EnvioDocumentosCorreo
{
    /// <summary>
    /// NestoAPI#724: implementación real (BD + generadores de PDF). El PDF del pedido es la proforma que ya sale en
    /// «Descargar PDF» del detalle del pedido (GestorFacturas.LeerPedido + QuestPDF); el del mandato, el de
    /// api/Clientes/MandatoPDF.
    /// </summary>
    public class ProveedorDocumentosCorreo : IProveedorDocumentosCorreo
    {
        public async Task<DocumentoParaCorreo> PdfPedido(string empresa, int numero, string usuario)
        {
            string serie;
            string cliente;
            string contacto;
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                var cab = await db.CabPedidoVtas
                    .Where(c => c.Empresa == empresa && c.Número == numero)
                    .Select(c => new { c.Serie, c.Nº_Cliente, c.Contacto })
                    .FirstOrDefaultAsync().ConfigureAwait(false);
                if (cab == null)
                {
                    return null;
                }
                serie = cab.Serie;
                cliente = cab.Nº_Cliente?.Trim();
                contacto = cab.Contacto?.Trim();
            }

            var gestor = new GestorFacturas();
            Factura pedido = gestor.LeerPedido(empresa, numero);
            byte[] pdf = await gestor.FacturasEnPDF(new List<Factura> { pedido }, false, usuario)
                .ReadAsByteArrayAsync().ConfigureAwait(false);
            ISerieFactura serieFactura = GestorFacturas.LeerSerie(serie?.Trim());
            return new DocumentoParaCorreo
            {
                Pdf = pdf,
                NombreFichero = $"Pedido_{numero}.pdf",
                Cliente = cliente,
                Contacto = contacto,
                Remitente = serieFactura?.CorreoDesdeFactura ?? new MailAddress(Constantes.Correos.CORREO_ADMON),
                Firma = serieFactura?.FirmaCorreo
            };
        }

        public async Task<DocumentoParaCorreo> PdfMandato(string empresa, string cliente, string contacto, string ccc, string motor)
        {
            var servicio = new ServicioGestorClientes();
            Cliente ficha = await servicio.BuscarCliente(empresa, cliente, contacto).ConfigureAwait(false);
            if (ficha == null)
            {
                return null;
            }
            string cuenta = string.IsNullOrWhiteSpace(ccc) ? ficha.CCC?.Trim() : ccc.Trim();
            if (string.IsNullOrEmpty(cuenta) || await servicio.BuscarCCC(empresa, cliente, contacto, cuenta).ConfigureAwait(false) == null)
            {
                return null;
            }

            // Solo usa el servicio de BD: ni agencias ni sincronización
            var gestorClientes = new GestorClientes(servicio, null, null);
            Mandato mandato = await gestorClientes.LeerMandato(empresa, cliente, contacto, cuenta).ConfigureAwait(false);
            if (!mandato.Iban.EsValido)
            {
                throw new Exceptions.NestoBusinessException($"El IBAN {mandato.Iban.Formateado} no es válido");
            }
            byte[] pdf = await gestorClientes.MandatoEnPDF(new List<Mandato> { mandato }, string.IsNullOrWhiteSpace(motor) ? "QuestPDF" : motor)
                .ReadAsByteArrayAsync().ConfigureAwait(false);
            ISerieFactura serieNV = GestorFacturas.LeerSerie("NV");
            return new DocumentoParaCorreo
            {
                Pdf = pdf,
                NombreFichero = $"MandatoSEPA_{cliente}_{cuenta}.pdf",
                Cliente = cliente,
                Contacto = contacto,
                Remitente = new MailAddress(Constantes.Correos.CORREO_ADMON),
                Firma = serieNV?.FirmaCorreo
            };
        }

        public async Task<List<string>> CorreosContacto(string empresa, string cliente, string contacto)
        {
            using (var db = new NVEntities())
            {
                List<string> correos = await db.PersonasContactoClientes
                    .Where(p => p.Empresa == empresa && p.NºCliente == cliente && p.Contacto == contacto
                        && p.Estado >= Constantes.Clientes.PersonasContacto.ESTADO_POR_DEFECTO
                        && p.CorreoElectrónico != null)
                    .OrderBy(p => p.Número)
                    .Select(p => p.CorreoElectrónico)
                    .ToListAsync().ConfigureAwait(false);
                return GestorEnvioDocumentosCorreo.CorreosValidosSinRepetir(correos);
            }
        }

        public string CorreoUsuario(string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return null;
            }
            string sinDominio = usuario.Substring(usuario.IndexOf('\\') + 1).Trim();
            using (var db = new NVEntities())
            {
                string valor = db.ParametrosUsuario
                    .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == sinDominio
                        && p.Clave == Parametros.Claves.CorreoDefecto)
                    .Select(p => p.Valor)
                    .FirstOrDefault();
                return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
            }
        }

        public async Task RegistrarEnvio(string tabla, string documento, string descripcion, string usuario)
        {
            using (var db = new NVEntities())
            {
                _ = db.Modificaciones.Add(new Modificacion
                {
                    Tabla = tabla,
                    Anterior = documento,
                    Nuevo = descripcion,
                    Usuario = usuario
                });
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }
}
