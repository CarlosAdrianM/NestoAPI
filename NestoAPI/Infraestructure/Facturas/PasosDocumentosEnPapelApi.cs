using NestoAPI.Controllers;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// NestoAPI#828: los pasos reales del papel del packing. Factura con la MISMA acción que Agencias de Nesto y F8 de Ariadna
    /// (<see cref="FacturacionRutasController.FacturarPedido"/>, con el usuario de la petición), regenera con
    /// <see cref="GestorFacturacionRutas.ObtenerDocumentosImpresion"/> y marca en <see cref="IRepositorioDocumentosPapelPacking"/>.
    /// Un objeto por petición: se libera al acabar.
    /// </summary>
    public sealed class PasosDocumentosEnPapelApi : IPasosDocumentosEnPapel, IDisposable
    {
        private readonly IPrincipal usuario;
        private readonly bool saltarPacking;
        private readonly IRepositorioDocumentosPapelPacking marcas;
        private NVEntities db;

        public PasosDocumentosEnPapelApi(IPrincipal usuario, bool saltarPacking = false, IRepositorioDocumentosPapelPacking marcas = null)
        {
            this.usuario = usuario;
            this.saltarPacking = saltarPacking;
            this.marcas = marcas ?? new RepositorioDocumentosPapelPacking();
        }

        private NVEntities Db
        {
            get
            {
                if (db == null)
                {
                    db = new NVEntities();
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                }
                return db;
            }
        }

        private string UsuarioAuditoria => UsuarioAuditoriaHelper.Resolver(usuario, "NestoAPI");

        public async Task<FacturarRutasResponseDTO> Facturar(string empresa, int pedido)
        {
            using (var facturacion = new FacturacionRutasController())
            {
                facturacion.Request = new HttpRequestMessage();
                facturacion.Configuration = new HttpConfiguration();
                facturacion.User = usuario;
                IHttpActionResult resultado = await facturacion.FacturarPedido(new FacturarPedidoRequestDTO
                {
                    Empresa = empresa,
                    Pedido = pedido,
                    SaltarPacking = saltarPacking
                });
                if (resultado is OkNegotiatedContentResult<FacturarRutasResponseDTO> ok)
                {
                    return ok.Content;
                }
                HttpResponseMessage respuesta = await resultado.ExecuteAsync(CancellationToken.None);
                if (respuesta.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new InvalidOperationException("Su usuario no tiene permiso para facturar.");
                }
                string cuerpo = respuesta.Content == null ? null : await respuesta.Content.ReadAsStringAsync();
                throw new InvalidOperationException(MensajeDe(cuerpo) ?? $"La facturación ha contestado {(int)respuesta.StatusCode}.");
            }
        }

        public Task<bool> EsNotaDeRepartoPropio(string empresa, int pedido)
            => new GuardiaNotaSinPacking(new RepositorioEstadoNotaPacking(Db)).EsNotaDeRepartoPropio(empresa, pedido);

        public async Task<string> RutaDelPedido(string empresa, int pedido)
        {
            List<string> rutas = await Db.CabPedidoVtas.AsNoTracking()
                .Where(c => c.Empresa == empresa && c.Número == pedido)
                .Select(c => c.Ruta)
                .ToListAsync();
            return rutas.FirstOrDefault()?.Trim();
        }

        public Task<List<DocumentoEnPapelDTO>> LeerMarcas(string empresa, int pedido) => marcas.Leer(empresa, pedido);

        public Task Marcar(DocumentoEnPapelDTO marca, string aplicacion) => marcas.Guardar(marca, aplicacion, UsuarioAuditoria);

        public Task<DocumentosImpresionPedidoDTO> Regenerar(string empresa, int pedido, string tipo, string documento)
        {
            var servicioFacturas = new ServicioFacturas(Db);
            var gestor = new GestorFacturacionRutas(Db, new ServicioAlbaranesVenta(Db), servicioFacturas, new GestorFacturas(servicioFacturas),
                new ServicioTraspasoEmpresa(Db), new ServicioNotasEntrega(Db), new ServicioExtractoRuta(Db));
            string factura = tipo == TiposDocumentoEnPapel.FACTURA ? documento?.Trim() : null;
            int? albaran = tipo == TiposDocumentoEnPapel.ALBARAN && int.TryParse(documento?.Trim(), out int numero) ? numero : (int?)null;
            return gestor.ObtenerDocumentosImpresion(empresa, pedido, factura, albaran, UsuarioAuditoria);
        }

        public void Dispose() => db?.Dispose();

        /// <summary>El «Message» de un HttpError, el texto de un Content(estado, "texto") o el cuerpo tal cual.</summary>
        internal static string MensajeDe(string cuerpo)
        {
            if (string.IsNullOrWhiteSpace(cuerpo))
            {
                return null;
            }
            try
            {
                JToken token = JToken.Parse(cuerpo);
                if (token.Type == JTokenType.String)
                {
                    return token.Value<string>();
                }
                if (token is JObject objeto && objeto.GetValue("Message", StringComparison.OrdinalIgnoreCase) is JToken mensaje && mensaje.Type == JTokenType.String)
                {
                    return mensaje.Value<string>();
                }
            }
            catch (JsonException)
            {
                // No es JSON: el texto tal cual.
            }
            return cuerpo.Trim();
        }
    }
}
