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
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.PreparacionAlmacen;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 5): los pasos reales de la etiqueta del packing. NO repite la lógica de las agencias: llama a las
    /// mismas acciones que usa Agencias de Nesto (DesdePedido, Tramitar, ImprimirEtiqueta de <see cref="EnviosAgenciasController"/>
    /// y FacturarPedido de <see cref="FacturacionRutasController"/>) con el usuario de la petición, sobre un mismo contexto de
    /// EF para todo el envío (Tramitar ve lo que acaba de grabar DesdePedido). Un objeto por petición: se libera al acabar.
    /// </summary>
    public sealed class PasosEtiquetaPackingApi : IPasosEtiquetaPacking
    {
        private readonly IPrincipal usuario;
        private readonly IServicioPreparacionAlmacen preparacion;
        private readonly EnviosAgenciasController envios;

        public PasosEtiquetaPackingApi(IPrincipal usuario, IServicioPreparacionAlmacen preparacion)
            : this(usuario, preparacion, new EnviosAgenciasController())
        {
        }

        /// <summary>Para los tests: el controlador de envíos con su BD y sus agencias falsas.</summary>
        internal PasosEtiquetaPackingApi(IPrincipal usuario, IServicioPreparacionAlmacen preparacion, EnviosAgenciasController envios)
        {
            this.usuario = usuario;
            this.preparacion = preparacion;
            this.envios = Preparar(envios);
        }

        private T Preparar<T>(T controller) where T : ApiController
        {
            controller.Request = new HttpRequestMessage();
            controller.Configuration = new HttpConfiguration();
            controller.User = usuario;
            return controller;
        }

        private NVEntities Db => envios.BaseDeDatos;

        public async Task<EnviosAgencia> LeerEnvioEnCurso(string empresa, int pedido)
        {
            // Where + ToListAsync (no FirstOrDefaultAsync): ver el «Limit1» de TramitacionEnviosService. Con seguimiento: Tramitar
            // lo vuelve a buscar en el mismo contexto.
            // NestoAPI#803: también el del reparto propio que ya registró un intento anterior de hoy (Tramitar lo deja Tramitado
            // en el acto): si no, al repetir la etiqueta (la impresora falló) parecería una ampliación.
            DateTime hoy = DateTime.Today;
            List<EnviosAgencia> enCurso = await Db.EnviosAgencias
                .Where(e => e.Pedido == pedido && (e.Estado == Constantes.Agencias.ESTADO_EN_CURSO
                    || (e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO && e.Estado == Constantes.Agencias.ESTADO_TRAMITADO
                        && e.Fecha >= hoy)))
                .ToListAsync();
            return EnvioDelIntentoAnterior(enCurso, hoy);
        }

        /// <summary>
        /// El «envío actual» del pedido para la etiqueta del packing: el último en curso (Estado 0) o, NestoAPI#803, el último
        /// del reparto propio ya registrado (Tramitado, con albarán RP) con fecha de hoy o posterior, que se reimprime.
        /// </summary>
        internal static EnviosAgencia EnvioDelIntentoAnterior(IEnumerable<EnviosAgencia> envios, DateTime hoy)
            => (envios ?? Enumerable.Empty<EnviosAgencia>())
                .Where(e => e != null && (e.Estado == Constantes.Agencias.ESTADO_EN_CURSO
                    || (e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO && e.Estado == Constantes.Agencias.ESTADO_TRAMITADO
                        && e.Fecha.Date >= hoy.Date && !string.IsNullOrWhiteSpace(e.CodigoBarras))))
                .OrderByDescending(e => e.Numero)
                .FirstOrDefault();

        public async Task<EntregaParaEtiqueta> LeerEntrega(string empresa, int pedido)
        {
            if (preparacion == null)
            {
                return null;
            }
            PackingAlmacenDTO delPedido = await preparacion.LeerPackingDePedido(empresa, pedido);
            if (delPedido != null && delPedido.Picking > 0)
            {
                // El del picking entero: la entrega lleva también los demás pedidos del cliente y sus cajas
                EntregaParaEtiqueta entrega = EntregaParaEtiqueta.De(await preparacion.LeerPacking(empresa, delPedido.Picking), pedido);
                if (entrega != null)
                {
                    return entrega;
                }
            }
            List<BultoAlmacenDTO> bultos = await preparacion.LeerBultos(empresa, pedido);
            return bultos == null || bultos.Count == 0 ? null : EntregaParaEtiqueta.De(bultos);
        }

        public Task<ResultadoEnvioDesdePedido> CrearDesdePedido(CrearEnvioDesdePedidoDTO peticion, string usuarioAuditoria)
            => envios.EnvioDesdePedido.Crear(peticion, usuarioAuditoria);

        public async Task<PasoEtiquetaAgencia> Tramitar(int envio)
        {
            HttpResponseMessage respuesta = await Ejecutar(await envios.TramitarEnvio(envio));
            string cuerpo = await Cuerpo(respuesta);
            if (!respuesta.IsSuccessStatusCode)
            {
                return PasoEtiquetaAgencia.Mal(respuesta.StatusCode, MensajeDe(cuerpo));
            }
            TramitarEnvioResultadoDTO resultado = Leer<TramitarEnvioResultadoDTO>(cuerpo);
            return PasoEtiquetaAgencia.Bien(resultado?.Albaran, resultado?.Bultos);
        }

        public async Task<PasoEtiquetaAgencia> Imprimir(int envio, string impresora)
        {
            HttpResponseMessage respuesta = await Ejecutar(await envios.ImprimirEtiqueta(envio, new ImprimirEtiquetaEnvioDTO { Impresora = impresora }));
            string cuerpo = await Cuerpo(respuesta);
            if (!respuesta.IsSuccessStatusCode)
            {
                PasoEtiquetaAgencia mal = PasoEtiquetaAgencia.Mal(respuesta.StatusCode, MensajeDe(cuerpo));
                mal.Impresora = impresora;
                return mal;
            }
            ImprimirEtiquetaEnvioResultadoDTO resultado = Leer<ImprimirEtiquetaEnvioResultadoDTO>(cuerpo);
            return PasoEtiquetaAgencia.Bien(resultado?.CodigoBarras, resultado?.Bultos, resultado?.Impresora ?? impresora);
        }

        public async Task<FacturarRutasResponseDTO> Facturar(string empresa, int pedido)
        {
            using (FacturacionRutasController facturacion = Preparar(new FacturacionRutasController()))
            {
                IHttpActionResult resultado = await facturacion.FacturarPedido(new FacturarPedidoRequestDTO { Empresa = empresa, Pedido = pedido });
                if (resultado is OkNegotiatedContentResult<FacturarRutasResponseDTO> ok)
                {
                    return ok.Content;
                }
                HttpResponseMessage respuesta = await Ejecutar(resultado);
                if (respuesta.StatusCode == HttpStatusCode.Forbidden)
                {
                    throw new InvalidOperationException("Su usuario no tiene permiso para facturar.");
                }
                throw new InvalidOperationException(MensajeDe(await Cuerpo(respuesta)) ?? $"La facturación ha contestado {(int)respuesta.StatusCode}.");
            }
        }

        public async Task<string> NombreAgencia(int agencia)
        {
            List<string> nombres = await Db.AgenciasTransportes.AsNoTracking().Where(a => a.Numero == agencia).Select(a => a.Nombre).ToListAsync();
            return nombres.FirstOrDefault()?.Trim();
        }

        public Task<bool> EsNotaDeRepartoPropio(string empresa, int pedido)
            => new NotasEntrega.GuardiaNotaSinPacking(new NotasEntrega.RepositorioEstadoNotaPacking(Db)).EsNotaDeRepartoPropio(empresa, pedido);

        // NestoAPI#828: el papel del packing, con los pasos comunes (los mismos que la etiqueta de Nesto).
        private Facturas.PasosDocumentosEnPapelApi papel;
        private Facturas.PasosDocumentosEnPapelApi Papel => papel ?? (papel = new Facturas.PasosDocumentosEnPapelApi(usuario));

        public Task<string> RutaDelPedido(string empresa, int pedido) => Papel.RutaDelPedido(empresa, pedido);

        public Task<List<DocumentoEnPapelDTO>> LeerMarcas(string empresa, int pedido) => Papel.LeerMarcas(empresa, pedido);

        public Task Marcar(DocumentoEnPapelDTO marca, string aplicacion) => Papel.Marcar(marca, aplicacion);

        public Task<DocumentosImpresionPedidoDTO> Regenerar(string empresa, int pedido, string tipo, string documento)
            => Papel.Regenerar(empresa, pedido, tipo, documento);

        public Task<List<CartaChequeRegaloDTO>> RegenerarCartas(string empresa, int pedido, string factura)
            => Papel.RegenerarCartas(empresa, pedido, factura);

        public void Dispose()
        {
            envios.Dispose();
            papel?.Dispose();
        }

        // ---------------------------------------------------------------- piezas

        private static Task<HttpResponseMessage> Ejecutar(IHttpActionResult resultado)
            => resultado.ExecuteAsync(CancellationToken.None);

        private static async Task<string> Cuerpo(HttpResponseMessage respuesta)
            => respuesta.Content == null ? null : await respuesta.Content.ReadAsStringAsync();

        private static T Leer<T>(string cuerpo) where T : class
        {
            try
            {
                return string.IsNullOrWhiteSpace(cuerpo) ? null : JsonConvert.DeserializeObject<T>(cuerpo);
            }
            catch (JsonException)
            {
                return null;
            }
        }

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
                if (token is JObject objeto)
                {
                    JToken mensaje = objeto.GetValue("Message", StringComparison.OrdinalIgnoreCase);
                    if (mensaje != null && mensaje.Type == JTokenType.String)
                    {
                        return mensaje.Value<string>();
                    }
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
