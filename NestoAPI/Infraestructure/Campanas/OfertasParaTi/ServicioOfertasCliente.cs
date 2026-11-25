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

    /// <summary>Lo que hace el endpoint GET api/Pedidos/Cliente/Ofertas (sustituible en las pruebas del controlador).</summary>
    public interface IServicioOfertasCliente
    {
        /// <summary>Nunca lanza: si algo falla, sin ofertas (y a ELMAH).</summary>
        Task<OfertasParaTiRespuestaDTO> Calcular(string cliente, string campana, int? maximo, PoliticaPreciosOcultos.NivelPrecios nivel, DateTime hoy);
    }

    /// <summary>
    /// «Ofertas para ti» de UN cliente de la app: el núcleo (<see cref="ServicioOfertasParaTi"/>) le da sus candidatas ordenadas y aquí se
    /// les pone el precio del motor (fuera las que no mejoran sin la campaña), la foto de la tienda y la política de precios ocultos.
    /// </summary>
    public class ServicioOfertasCliente : IServicioOfertasCliente
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private readonly ServicioOfertasParaTi nucleo;
        private readonly Func<string, DateTime, Task<ContextoOfertasParaTi>> contexto;
        private readonly ICalculadorPreciosOferta precios;
        private readonly Func<string, Task<string>> leerContacto;
        private readonly Func<string, Task<string>> leerFoto;
        private readonly Action<Exception> registrar;

        public ServicioOfertasCliente(ServicioOfertasParaTi nucleo, ICalculadorPreciosOferta precios, Func<string, Task<string>> leerContacto,
            Func<string, Task<string>> leerFoto = null, Func<string, DateTime, Task<ContextoOfertasParaTi>> contexto = null, Action<Exception> registrar = null)
        {
            this.nucleo = nucleo ?? throw new ArgumentNullException(nameof(nucleo));
            this.precios = precios ?? throw new ArgumentNullException(nameof(precios));
            this.leerContacto = leerContacto ?? throw new ArgumentNullException(nameof(leerContacto));
            this.leerFoto = leerFoto ?? RecordatorioReposicionJobsService.LeerFotoTienda;
            this.contexto = contexto ?? ((c, d) => nucleo.Preparar(c, d));
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        /// <summary>La de producción: con el contexto de la campaña guardado 10 minutos para todos los clientes.</summary>
        public static ServicioOfertasCliente Produccion(NVEntities db)
        {
            var nucleo = new ServicioOfertasParaTi(new LectorOfertasParaTiSql(db),
                new PuntuadorReposicionModelo(new SugerenciasProductos.RepositorioSugerenciasProductosSql(db),
                    SugerenciasProductos.PredictorSugerenciasProductos.Instancia));
            return new ServicioOfertasCliente(nucleo, new CalculadorPreciosOfertaMotor(db),
                async cliente => (await System.Data.Entity.QueryableExtensions.AsNoTracking(db.Clientes)
                    .BuscarPrincipalAsync(EMPRESA, cliente).ConfigureAwait(false))?.Contacto?.Trim(),
                contexto: (c, d) => CacheContextoOfertasParaTi.Obtener(c, d, () => nucleo.Preparar(c, d)));
        }

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
                    Dictionary<string, ResultadoOfertasCliente> calculado = await nucleo.Calcular(ctx, new[] { cliente }, null).ConfigureAwait(false);
                    List<OfertaElegida> ordenadas = calculado.TryGetValue(cliente.Trim(), out ResultadoOfertasCliente r) ? r.Ofertas : new List<OfertaElegida>();
                    foreach (OfertaElegida o in ordenadas)
                    {
                        if (respuesta.Ofertas.Count >= n)
                        {
                            break;
                        }
                        PreciosOferta p;
                        try
                        {
                            p = await precios.Calcular(EMPRESA, cliente, contacto, o.Candidata.Producto, o.Candidata.Campana).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            registrar(new Exception($"[Ofertas para ti] No se ha podido calcular el precio del {o.Candidata.Producto} para el cliente {cliente}: {ex.Message}", ex));
                            continue;
                        }
                        if (!ReglaPreciosOfertaParaTi.Mejora(p))
                        {
                            continue; // con la campaña no paga menos (su precio de siempre ya es mejor): no es una oferta para él
                        }
                        var oferta = new OfertaParaTiDTO
                        {
                            Producto = o.Candidata.Producto,
                            Nombre = ReglaOfertasParaTi.FormatoTitulo(o.Candidata.Nombre),
                            Familia = ReglaOfertasParaTi.FormatoTitulo(string.IsNullOrWhiteSpace(o.Candidata.NombreFamilia) ? o.Candidata.Familia : o.Candidata.NombreFamilia),
                            Campana = o.Candidata.Campana,
                            CantidadSugerida = Math.Max(1, o.CantidadSugerida),
                            Motivo = o.Motivo,
                            Texto = o.Texto
                        };
                        ReglaPreciosOfertaParaTi.AplicarNivel(oferta, p, nivel);
                        respuesta.Ofertas.Add(oferta);
                    }
                    await PonerFotos(respuesta.Ofertas).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Ofertas para ti] No se han podido calcular las ofertas del cliente {cliente}: {ex.Message}", ex));
                respuesta.Ofertas.Clear();
            }

            respuesta.HayOfertas = respuesta.Ofertas.Count > 0;
            string principal = ctx?.CampanaPedida
                ?? respuesta.Ofertas.FirstOrDefault()?.Campana
                ?? ReglaOfertasParaTi.CampanaPrincipal(ctx?.Candidatas)
                ?? (string.IsNullOrWhiteSpace(campana) ? null : campana.Trim());
            respuesta.Campana = principal;
            respuesta.Titulo = ReglaOfertasParaTi.Titulo(principal);
            DateTime? fin = ReglaOfertasParaTi.FinDeCampana(ctx?.Candidatas, principal);
            respuesta.FechaHasta = fin?.ToString("yyyy-MM-dd");
            respuesta.TextoHasta = ReglaOfertasParaTi.TextoHasta(fin, hoy);
            return respuesta;
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
