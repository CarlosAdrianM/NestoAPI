using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.PedidosVenta.CondicionesCampana;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>
    /// «Ofertas para ti»: los precios de una oferta y lo que se enseña según la política de precios ocultos (#446). Pura.
    /// </summary>
    public static class ReglaPreciosOfertaParaTi
    {
        /// <summary>Solo se enseña si con la campaña paga al menos un céntimo menos que sin ella.</summary>
        public static bool Mejora(PreciosOferta precios)
            => precios != null && precios.PrecioAntes > 0 && precios.PrecioAntes - precios.Precio >= 0.005m;

        /// <summary>1 − Precio/PrecioAntes a 4 decimales (0 si no hay precio antes).</summary>
        public static decimal Descuento(PreciosOferta precios)
            => precios == null || precios.PrecioAntes <= 0 ? 0
                : Math.Round(1 - precios.Precio / precios.PrecioAntes, 4, MidpointRounding.AwayFromZero);

        /// <summary>
        /// Completo: los tres. SinDescuentos (cargo 31): en Precio la tarifa profesional, sin PrecioAntes ni Descuento (como
        /// <see cref="PoliticaPreciosOcultos.AplicarNivel"/>). SinPrecios (cargo 30): ningún importe.
        /// </summary>
        public static void AplicarNivel(OfertaParaTiDTO oferta, PreciosOferta precios, PoliticaPreciosOcultos.NivelPrecios nivel)
        {
            if (oferta == null || precios == null)
            {
                return;
            }
            switch (nivel)
            {
                case PoliticaPreciosOcultos.NivelPrecios.SinPrecios:
                    oferta.Precio = null;
                    oferta.PrecioAntes = null;
                    oferta.Descuento = null;
                    break;
                case PoliticaPreciosOcultos.NivelPrecios.SinDescuentos:
                    oferta.Precio = Redondear(precios.Tarifa);
                    oferta.PrecioAntes = null;
                    oferta.Descuento = null;
                    break;
                default:
                    oferta.Precio = Redondear(precios.Precio);
                    oferta.PrecioAntes = Redondear(precios.PrecioAntes);
                    oferta.Descuento = Descuento(precios);
                    break;
            }
        }

        private static decimal Redondear(decimal importe) => Math.Round(importe, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Los dos precios de una oferta para un cliente. Null si no se puede (producto que no existe…). Puede lanzar.</summary>
    public interface ICalculadorPreciosOferta
    {
        Task<PreciosOferta> Calcular(string empresa, string cliente, string contacto, string producto, string campana);
    }

    /// <summary>
    /// El motor de siempre: el precio con la campaña por el MISMO camino que la app (<c>ProductosController.GetProductoPrecioReal</c>,
    /// el de GET api/Productos con cliente, 1 unidad) y el de sin la campaña por <see cref="CalculadorPrecioSinCampanaMotor"/>
    /// (<c>PrecioDescuentoProducto.campanaExcluida</c>, con la ficha que la campaña desbloquea sin descuento, #889).
    /// </summary>
    public class CalculadorPreciosOfertaMotor : ICalculadorPreciosOferta
    {
        private readonly NVEntities db;
        private readonly ICalculadorPrecioSinCampana sinCampana;
        private readonly Func<Task<List<FilaCampanaAplicarDto>>> leerDesbloqueos;
        private List<FilaCampanaAplicarDto> desbloqueos;

        public CalculadorPreciosOfertaMotor(NVEntities db, ICalculadorPrecioSinCampana sinCampana = null, Func<Task<List<FilaCampanaAplicarDto>>> leerDesbloqueos = null)
        {
            this.db = db;
            this.sinCampana = sinCampana ?? new CalculadorPrecioSinCampanaMotor();
            this.leerDesbloqueos = leerDesbloqueos ?? (() => new RepositorioCampanasAplicarDto().LeerPendientes());
        }

        public async Task<PreciosOferta> Calcular(string empresa, string cliente, string contacto, string producto, string campana)
        {
            // Sin using: comparte el contexto (como PedidosClienteController.CalcularPrecios)
            IHttpActionResult resultado = await new ProductosController(db).GetProductoPrecioReal(empresa, producto, cliente, contacto, 1)
                .ConfigureAwait(false);
            if (!(resultado is OkNegotiatedContentResult<ProductoPlantillaDTO> ok) || ok.Content == null)
            {
                return null;
            }
            if (desbloqueos == null)
            {
                try
                {
                    desbloqueos = await leerDesbloqueos().ConfigureAwait(false) ?? new List<FilaCampanaAplicarDto>();
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Ofertas para ti] No se han podido leer las fichas desbloqueadas por la campaña: {ex.Message}", ex));
                    desbloqueos = new List<FilaCampanaAplicarDto>();
                }
            }
            PrecioSinCampana antes = sinCampana.Calcular(empresa, cliente, contacto, producto, campana, 1,
                ReglaCampanaSoloEnStock.FichaDesbloqueadaPorLaCampana(desbloqueos, campana, empresa, producto, DateTime.Today));
            if (antes == null)
            {
                return null;
            }
            return new PreciosOferta
            {
                Precio = ok.Content.precio * (1 - ok.Content.descuento),
                PrecioAntes = antes.PrecioEfectivo,
                Tarifa = antes.Pvp
            };
        }
    }

    /// <summary>Lo que hacen los endpoints GET api/Pedidos/Cliente/Ofertas y …/Ofertas/Todas (sustituible en las pruebas del controlador).</summary>
    public interface IServicioOfertasCliente
    {
        /// <summary>Nunca lanza: si algo falla, sin ofertas (y a ELMAH).</summary>
        Task<OfertasParaTiRespuestaDTO> Calcular(string cliente, string campana, int? maximo, PoliticaPreciosOcultos.NivelPrecios nivel, DateTime hoy);

        /// <summary>«Ver todas»: una página del ranking del cliente. Nunca lanza: si algo falla, la página vacía (y a ELMAH).</summary>
        Task<TodasOfertasParaTiRespuestaDTO> Todas(string cliente, string campana, int desde, int? cuantas, string familia, string texto,
            PoliticaPreciosOcultos.NivelPrecios nivel, DateTime hoy);

        /// <summary>Lo que el cliente ha hecho con las ofertas (en sombra, en segundo plano). Nunca lanza.</summary>
        void RegistrarEventos(string cliente, PeticionEventosOfertasDTO peticion, DateTime ahora);
    }

    /// <summary>
    /// «Ofertas para ti» de UN cliente de la app: el núcleo (<see cref="ServicioOfertasParaTi"/>) le da su ranking (guardado con el
    /// contexto de la campaña), el selector (<see cref="SelectorOfertasConPrecio"/>) lo recorre quedándose con lo que de verdad le mejora
    /// el precio y aquí se pone la foto de la tienda y la política de precios ocultos.
    /// </summary>
    public class ServicioOfertasCliente : IServicioOfertasCliente
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private readonly ServicioOfertasParaTi nucleo;
        private readonly Func<string, DateTime, Task<ContextoOfertasParaTi>> contexto;
        private readonly SelectorOfertasConPrecio selector;
        private readonly Func<string, Task<string>> leerContacto;
        private readonly Func<string, Task<string>> leerFoto;
        private readonly Action<Exception> registrar;
        private readonly IRegistroOfertasMostradas registro;

        /// <param name="registro">Dónde se apunta lo servido y lo que hace el cliente (en sombra). Sin él, no se apunta nada.</param>
        public ServicioOfertasCliente(ServicioOfertasParaTi nucleo, ICalculadorPreciosOferta precios, Func<string, Task<string>> leerContacto,
            Func<string, Task<string>> leerFoto = null, Func<string, DateTime, Task<ContextoOfertasParaTi>> contexto = null, Action<Exception> registrar = null,
            IRegistroOfertasMostradas registro = null)
        {
            this.registro = registro;
            this.nucleo = nucleo ?? throw new ArgumentNullException(nameof(nucleo));
            this.leerContacto = leerContacto ?? throw new ArgumentNullException(nameof(leerContacto));
            this.leerFoto = leerFoto ?? RecordatorioReposicionJobsService.LeerFotoTienda;
            this.contexto = contexto ?? ((c, d) => nucleo.Preparar(c, d));
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
            selector = new SelectorOfertasConPrecio(nucleo, precios ?? throw new ArgumentNullException(nameof(precios)), this.registrar);
        }

        /// <summary>
        /// La de producción: con el contexto de la campaña guardado 10 minutos para todos los clientes; con él se guardan el ranking y los
        /// precios ya calculados de cada cliente (la franja, la pantalla y «Ver todas» no los repiten).
        /// </summary>
        public static ServicioOfertasCliente Produccion(NVEntities db)
        {
            ServicioOfertasParaTi nucleo = ServicioOfertasParaTi.Produccion(db);
            return new ServicioOfertasCliente(nucleo, new CalculadorPreciosOfertaMotor(db), LectorContactoPrincipal(db),
                contexto: (c, d) => CacheContextoOfertasParaTi.Obtener(c, d, () => nucleo.Preparar(c, d)), registro: RegistroOfertasMostradas.Produccion());
        }

        /// <summary>El contacto de la ficha principal del cliente (empresa 1); null si no existe.</summary>
        internal static Func<string, Task<string>> LectorContactoPrincipal(NVEntities db)
            => async cliente => (await System.Data.Entity.QueryableExtensions.AsNoTracking(db.Clientes)
                .BuscarPrincipalAsync(EMPRESA, cliente).ConfigureAwait(false))?.Contacto?.Trim();

        public async Task<OfertasParaTiRespuestaDTO> Calcular(string cliente, string campana, int? maximo, PoliticaPreciosOcultos.NivelPrecios nivel, DateTime hoy)
        {
            var respuesta = new OfertasParaTiRespuestaDTO
            {
                PrecioOculto = nivel == PoliticaPreciosOcultos.NivelPrecios.SinPrecios,
                DescuentoOculto = nivel == PoliticaPreciosOcultos.NivelPrecios.SinDescuentos
            };
            ContextoOfertasParaTi ctx = null;
            try
            {
                ctx = await contexto(campana, hoy.Date).ConfigureAwait(false);
                int n = ReglaOfertasParaTi.Maximo(maximo);
                string contacto = await leerContacto(cliente).ConfigureAwait(false);
                if (ctx != null && ctx.Candidatas.Count > 0 && !string.IsNullOrWhiteSpace(contacto))
                {
                    ResultadoOfertasCliente r = await Ranking(ctx, cliente).ConfigureAwait(false);
                    RecorridoOfertas elegidas = await selector.Seleccionar(ctx, r, EMPRESA, cliente, contacto, n).ConfigureAwait(false);
                    respuesta.Ofertas.AddRange(elegidas.Ofertas.Select(e => ADto(e, nivel)));
                    await PonerFotos(respuesta.Ofertas).ConfigureAwait(false);
                    // «Ver todas»: hay más ofertas de la campaña para él que las que se enseñan (apartadas por la diversidad o sin mirar)
                    respuesta.HayMasOfertas = respuesta.Ofertas.Count > 0 && (elegidas.Sobran || r.Ofertas.Count > respuesta.Ofertas.Count);
                    Apuntar(hoy, cliente, contacto, OrigenesOfertaMostrada.CINCO, elegidas, r);
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Ofertas para ti] No se han podido calcular las ofertas del cliente {cliente}: {ex.Message}", ex));
                respuesta.Ofertas.Clear();
                respuesta.HayMasOfertas = false;
            }

            respuesta.HayOfertas = respuesta.Ofertas.Count > 0;
            respuesta.Total = respuesta.Ofertas.Count;
            respuesta.Mensaje = respuesta.HayOfertas ? null : ReglaSeleccionOfertasParaTi.MENSAJE_SIN_OFERTAS;
            string principal = Principal(ctx, respuesta.Ofertas, campana);
            respuesta.Campana = principal;
            respuesta.Titulo = ReglaOfertasParaTi.Titulo(principal);
            DateTime? fin = ReglaOfertasParaTi.FinDeCampana(ctx?.Candidatas, principal);
            respuesta.FechaHasta = fin?.ToString("yyyy-MM-dd");
            respuesta.TextoHasta = ReglaOfertasParaTi.TextoHasta(fin, hoy);
            return respuesta;
        }

        public async Task<TodasOfertasParaTiRespuestaDTO> Todas(string cliente, string campana, int desde, int? cuantas, string familia, string texto,
            PoliticaPreciosOcultos.NivelPrecios nivel, DateTime hoy)
        {
            int inicio = Math.Max(0, desde);
            var respuesta = new TodasOfertasParaTiRespuestaDTO
            {
                PrecioOculto = nivel == PoliticaPreciosOcultos.NivelPrecios.SinPrecios,
                DescuentoOculto = nivel == PoliticaPreciosOcultos.NivelPrecios.SinDescuentos,
                Desde = inicio
            };
            ContextoOfertasParaTi ctx = null;
            try
            {
                ctx = await contexto(campana, hoy.Date).ConfigureAwait(false);
                respuesta.TotalCampana = ctx?.Candidatas.Count ?? 0;
                if (inicio == 0)
                {
                    respuesta.Familias = ReglaRankingOfertasParaTi.Familias(ctx?.Candidatas);
                }
                string contacto = await leerContacto(cliente).ConfigureAwait(false);
                if (ctx != null && ctx.Candidatas.Count > 0 && !string.IsNullOrWhiteSpace(contacto))
                {
                    ResultadoOfertasCliente r = await Ranking(ctx, cliente).ConfigureAwait(false);
                    List<OfertaElegida> filtradas = r.Ofertas.Where(o => ReglaRankingOfertasParaTi.PasaFiltro(o, familia, texto)).ToList();
                    RecorridoOfertas pagina = await selector.Recorrer(ctx, r, filtradas, inicio, EMPRESA, cliente, contacto,
                        ReglaSeleccionOfertasParaTi.CuantasTodas(cuantas), false, ReglaSeleccionOfertasParaTi.TOPE_EXAMINADOS_TODAS,
                        ReglaSeleccionOfertasParaTi.TOPE_EXAMINADOS_TODAS).ConfigureAwait(false);
                    respuesta.Ofertas.AddRange(pagina.Ofertas.Select(e => ADto(e, nivel)));
                    await PonerFotos(respuesta.Ofertas).ConfigureAwait(false);
                    respuesta.Siguiente = pagina.Siguiente;
                    Apuntar(hoy, cliente, contacto, OrigenesOfertaMostrada.TODAS, pagina, r);
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Ofertas para ti] No se han podido calcular todas las ofertas del cliente {cliente}: {ex.Message}", ex));
                respuesta.Ofertas.Clear();
                respuesta.Siguiente = null;
            }
            respuesta.HayMas = respuesta.Siguiente.HasValue;
            string principal = Principal(ctx, respuesta.Ofertas, campana);
            respuesta.Campana = principal;
            respuesta.Titulo = ReglaOfertasParaTi.Titulo(principal);
            DateTime? fin = ReglaOfertasParaTi.FinDeCampana(ctx?.Candidatas, principal);
            respuesta.FechaHasta = fin?.ToString("yyyy-MM-dd");
            respuesta.TextoHasta = ReglaOfertasParaTi.TextoHasta(fin, hoy);
            return respuesta;
        }

        public void RegistrarEventos(string cliente, PeticionEventosOfertasDTO peticion, DateTime ahora)
        {
            try
            {
                registro?.Eventos(EMPRESA, cliente, peticion, ahora);
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Ofertas para ti] No se han podido apuntar los eventos del cliente {cliente}: {ex.Message}", ex));
            }
        }

        /// <summary>Lo servido, al registro (en sombra; nunca rompe ni retrasa la respuesta).</summary>
        private void Apuntar(DateTime hoy, string cliente, string contacto, string origen, RecorridoOfertas servidas, ResultadoOfertasCliente r)
        {
            if (registro == null || servidas.Ofertas.Count == 0)
            {
                return;
            }
            try
            {
                registro.Servidas(ReglaRegistroOfertas.Filas(hoy, EMPRESA, cliente, contacto, origen, servidas.Ofertas, r.Ofertas, r.VersionModelo));
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Ofertas para ti] No se ha podido apuntar lo servido al cliente {cliente}: {ex.Message}", ex));
            }
        }

        private async Task<ResultadoOfertasCliente> Ranking(ContextoOfertasParaTi ctx, string cliente)
        {
            Dictionary<string, ResultadoOfertasCliente> calculado = await nucleo.Calcular(ctx, new[] { cliente }, null).ConfigureAwait(false);
            return calculado.TryGetValue(cliente.Trim(), out ResultadoOfertasCliente suyo) ? suyo : new ResultadoOfertasCliente { Cliente = cliente };
        }

        private static string Principal(ContextoOfertasParaTi ctx, List<OfertaParaTiDTO> ofertas, string campana)
            => ctx?.CampanaPedida
                ?? ofertas.FirstOrDefault()?.Campana
                ?? ReglaOfertasParaTi.CampanaPrincipal(ctx?.Candidatas)
                ?? (string.IsNullOrWhiteSpace(campana) ? null : campana.Trim());

        private static OfertaParaTiDTO ADto(OfertaConPrecio elegida, PoliticaPreciosOcultos.NivelPrecios nivel)
        {
            OfertaElegida o = elegida.Oferta;
            var oferta = new OfertaParaTiDTO
            {
                Producto = o.Candidata.Producto,
                Nombre = ReglaOfertasParaTi.FormatoTitulo(o.Candidata.Nombre),
                Familia = ReglaRankingOfertasParaTi.NombreFamilia(o.Candidata),
                Campana = o.Candidata.Campana,
                CantidadSugerida = Math.Max(1, o.CantidadSugerida),
                Motivo = o.Motivo,
                Texto = o.Texto
            };
            ReglaPreciosOfertaParaTi.AplicarNivel(oferta, elegida.Precios, nivel);
            return oferta;
        }

        private async Task PonerFotos(List<OfertaParaTiDTO> ofertas)
        {
            await Task.WhenAll(ofertas.Select(async o =>
            {
                try
                {
                    o.UrlFoto = PlantillaRecordatorioReposicion.NormalizarUrlFoto(await leerFoto(o.Producto).ConfigureAwait(false));
                }
                catch
                {
                    o.UrlFoto = null; // sin foto la oferta sigue sirviendo
                }
            })).ConfigureAwait(false);
        }
    }
}
