using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.DataHandler.Encoder;
using Microsoft.Owin.Security.Jwt;
using Microsoft.Owin.Security.OAuth;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Picking;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Alquileres;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.ExtractosRuta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Infraestructure.Videos;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Infraestructure.CanalesExternos.Amazon;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Comisiones;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PlanesVentajas;
using NestoAPI.Infraestructure.ServirJunto;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using NestoAPI.Providers;
using Newtonsoft.Json.Serialization;
using Owin;
using System;
using System.Net;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.Linq;
using System.Net.Http.Formatting;
using System.Web.Http;
using Hangfire;
using Hangfire.SqlServer;

namespace NestoAPI
{
    public class Startup
    {

        public void Configuration(IAppBuilder app)
        {
            try
            {
                ConfigurarTlsDelProceso();

                // NestoAPI#758: las rutas propias (AT, 16 y las que se añadan) salen del parámetro RutasRepartoPropio de la BD.
                Models.Facturas.RutasPropiasConfiguradas.UsarBaseDeDatos(ex => Infraestructure.ElmahHelper.Log(ex));
                // NestoAPI#764: el día de reparto de nuestra ruta por código postal (tabla CodigosPostalesDiaReparto).
                Infraestructure.PedidosVenta.DiasRepartoCodigosPostales.UsarBaseDeDatos(ex => Infraestructure.ElmahHelper.Log(ex));

                _ = app.UseCors(Microsoft.Owin.Cors.CorsOptions.AllowAll);

                HttpConfiguration httpConfig = new HttpConfiguration();

                // Configurar el contenedor de dependencias
                IServiceProvider serviceProvider = ConfigureServices();
                // Configurar el DependencyResolver
                httpConfig.DependencyResolver = new DependencyResolver(serviceProvider);

                // Configurar WebApi y pasarle el contenedor de dependencias
                WebApiConfig.Register(httpConfig);

                //// Configurar OWIN para usar el contenedor de dependencias
                //app.Use((context, next) =>
                //{
                //    // Establecer el proveedor de servicios en el contexto de OWIN
                //    context.Set<IServiceProvider>(serviceProvider);
                //    return next();
                //});

                ConfigureOAuthTokenGeneration(app);
                ConfigureOAuthTokenConsumption(app);

                // NestoAPI#536: avisos en tiempo real para Nesto (buzón, Nesto#477). DESPUÉS de la
                // autenticación JWT, para que el hub vea al usuario del token. NestoAPI#646: en la misma ruta, AlmacenHub
                // (las PDA de Ariadna que comparten recorrido o packing).
                _ = app.MapSignalR(Infraestructure.Notificaciones.AvisosTiempoReal.RUTA, new Microsoft.AspNet.SignalR.HubConfiguration());

                // Configurar Hangfire para jobs programados
                ConfigureHangfire(app);

                ConfigureWebApi(httpConfig);

                _ = app.UseWebApi(httpConfig);
            }
            catch (Exception ex)
            {
                // Escribir el error en el log de eventos de Windows
                System.Diagnostics.EventLog.WriteEntry("Application",
                    $"Error en NestoAPI Startup: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner Exception:\n{ex.InnerException?.Message}",
                    System.Diagnostics.EventLogEntryType.Error);

                // Re-lanzar para que se vea en Visual Studio
                throw;
            }
        }

        private void ConfigureOAuthTokenGeneration(IAppBuilder app)
        {
            // Configure the db context and user manager to use a single instance per request
            _ = app.CreatePerOwinContext(ApplicationDbContext.Create);
            _ = app.CreatePerOwinContext<ApplicationUserManager>(ApplicationUserManager.Create);
            _ = app.CreatePerOwinContext<ApplicationRoleManager>(ApplicationRoleManager.Create);

            OAuthAuthorizationServerOptions OAuthServerOptions = new OAuthAuthorizationServerOptions()
            {
                //For Dev enviroment only (on production should be AllowInsecureHttp = false)
                AllowInsecureHttp = true,
                TokenEndpointPath = new PathString("/oauth/token"),
                AccessTokenExpireTimeSpan = TimeSpan.FromDays(30),
                // NestoAPI#188: refresh_token OAuth2 para el flow de NestoApp (grant password).
                // El access_token sigue durando 30 días — no acortar hasta que NestoApp#117
                // (refresh transparente) esté desplegado, o los usuarios con token de más
                // de 30 min recibirán 401 sin saber refrescar.
                RefreshTokenProvider = new SimpleRefreshTokenProvider(TimeSpan.FromDays(90)),
                Provider = new CustomOAuthProvider(),
                //AccessTokenFormat = new CustomJwtFormat("http://localhost:53364")
                AccessTokenFormat = new CustomJwtFormat("carlos")
            };

            // OAuth 2.0 Bearer Access Token Generation
            _ = app.UseOAuthAuthorizationServer(OAuthServerOptions);
        }

        private void ConfigureOAuthTokenConsumption(IAppBuilder app)
        {

            //var issuer = "http://localhost:53364";
            string issuer = "carlos";
            string audienceId = ConfigurationManager.AppSettings["as:AudienceId"];
            byte[] audienceSecret = TextEncodings.Base64Url.Decode(ConfigurationManager.AppSettings["as:AudienceSecret"]);

            // Api controllers with an [Authorize] attribute will be validated with JWT
            _ = app.UseJwtBearerAuthentication(
                new JwtBearerAuthenticationOptions
                {
                    AuthenticationMode = AuthenticationMode.Active,
                    AllowedAudiences = new[] { audienceId },
                    IssuerSecurityKeyProviders = new IIssuerSecurityKeyProvider[] {
                        new SymmetricKeyIssuerSecurityKeyProvider(issuer, audienceSecret)
                    },
                    // NestoAPI#536: SignalR manda el JWT en la query string (access_token) al conectar.
                    // Solo se lee en /signalr y solo si no venía en la cabecera: el resto de rutas, igual.
                    Provider = new OAuthBearerAuthenticationProvider
                    {
                        OnRequestToken = contexto =>
                        {
                            contexto.Token = Infraestructure.Notificaciones.AvisosTiempoReal.TokenDeLaPeticion(
                                contexto.Request.Path, contexto.Token, contexto.Request.Query.Get("access_token"));
                            return System.Threading.Tasks.Task.FromResult(0);
                        }
                    }
                    // IMPORTANTE: NO usar TokenValidationParameters aquí.
                    // Si se proporciona TokenValidationParameters, OWIN ignora AllowedAudiences
                    // e IssuerSecurityKeyProviders, causando que todos los tokens se rechacen.
                    // Ver: StartupJwtConfigurationTests.cs para más detalles.
                    // El mapeo de usuario para ELMAH se hace en UserSyncHandler.
                });
        }

        private IServiceProvider ConfigureServices()
        {
            ServiceCollection services = new ServiceCollection();

            // Añadir logging
            _ = services.AddLogging(configure =>
            {
                _ = configure.AddEventLog(); // Opcionalmente, o AddEventLog en servidor
                _ = configure.AddDebug();   // Para ver en salida de VS
            });

            // Registrar tus dependencias aquí
            _ = services.AddScoped<IGestorClientes, GestorClientes>();
            _ = services.AddScoped<IServicioGestorClientes, ServicioGestorClientes>();
            _ = services.AddScoped<IServicioAgencias, ServicioAgencias>();
            _ = services.AddScoped<ISincronizacionEventPublisher, GooglePubSubEventPublisher>();
            _ = services.AddScoped<SincronizacionEventWrapper>();
            _ = services.AddScoped<IServicioVendedores, ServicioVendedores>();
            _ = services.AddScoped<ClientesController>();
            _ = services.AddScoped<IGestorAlbaranesVenta, GestorAlbaranesVenta>();
            _ = services.AddScoped<IServicioAlbaranesVenta, ServicioAlbaranesVenta>();
            _ = services.AddScoped<IServicioVideos, ServicioVideos>();
            _ = services.AddScoped<IServicioCorreoElectronico, ServicioCorreoElectronico>();
            _ = services.AddScoped<IServicioPlantillaVenta, ServicioPlantillaVenta>();

            // Servicios de Facturación de Rutas
            _ = services.AddScoped<IServicioPedidosParaFacturacion, ServicioPedidosParaFacturacion>();
            _ = services.AddScoped<IGestorFacturacionRutas, GestorFacturacionRutas>();
            _ = services.AddScoped<IServicioTraspasoEmpresa, ServicioTraspasoEmpresa>();
            _ = services.AddScoped<IServicioNotasEntrega, ServicioNotasEntrega>();
            _ = services.AddScoped<IServicioExtractoRuta, ServicioExtractoRuta>();

            // Servicios de Pagos y Redsys (Issues #93, #92, #59)
            _ = services.AddScoped<IRedsysService, RedsysService>();
            _ = services.AddScoped<IServicioReclamacionDeuda, ServicioReclamacionDeuda>();
            _ = services.AddScoped<IServicioPagos, ServicioPagos>();
            // NestoAPI#609: revisión (propuesta) del concepto de los enlaces de pago
            _ = services.AddScoped<IRevisorConcepto, RevisorConcepto>();
            _ = services.AddScoped<IContabilidadService, ContabilidadService>();
            // prdExtrProducto y PreExtrProducto, con la misma filosofía que ContabilidadService (un único punto de llamada)
            _ = services.AddScoped<Infraestructure.ExtractosProducto.IServicioExtractoProducto, Infraestructure.ExtractosProducto.ServicioExtractoProducto>();
            _ = services.AddScoped<ILectorParametrosUsuario, LectorParametrosUsuario>();

            // Registro centralizado de errores de clientes en ELMAH (ErroresController)
            _ = services.AddScoped<ILogService, ElmahLogService>();

            // Servicios de Notificaciones Push (Issue #108)
            _ = services.AddScoped<IServicioNotificacionesPush, ServicioNotificacionesPush>();

            // Ofertas autorizadas: lectura para NestoApp e informar a los vendedores bajo demanda (NestoAPI#233)
            _ = services.AddScoped<Infraestructure.OfertasAutorizadas.IServicioOfertasAutorizadas, Infraestructure.OfertasAutorizadas.ServicioOfertasAutorizadas>();

            // NestoAPI#641: el orden de recorrido por calles, una lectura por almacén y petición (picking, reposiciones,
            // pendiente de ubicar y huecos alternativos comparten la misma regla y el mismo lector)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.ILectorOrdenRecorrido>(_ => Infraestructure.PreparacionAlmacen.LectorOrdenRecorridoSql.ConContextoPropio());
            // Ariadna, la app de almacén: preparación de pedidos con lector y foto de bultos (NestoAPI#556)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioPreparacionAlmacen, Infraestructure.PreparacionAlmacen.ServicioPreparacionAlmacen>();
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioUbicacionesAlmacen, Infraestructure.PreparacionAlmacen.ServicioUbicacionesAlmacen>();
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioRecepcionCompras, Infraestructure.PreparacionAlmacen.ServicioRecepcionCompras>();
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioRecepcionReposiciones, Infraestructure.PreparacionAlmacen.ServicioRecepcionReposiciones>();
            // Recibir mercancía, un núcleo y una estrategia por tipo (NestoAPI#559 compras, #553 reposiciones)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioRecepcionCompras>(_ => Infraestructure.PreparacionAlmacen.RepositorioRecepcionCompras.ConContextoPropio());
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IAvisadorCompras, Infraestructure.PreparacionAlmacen.AvisadorCompras>();
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IOrigenRecepcion>(sp => new Infraestructure.PreparacionAlmacen.OrigenRecepcionCompras(
                sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IRepositorioRecepcionCompras>(),
                sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IAvisadorCompras>()));
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioCierreReposiciones>(_ => Infraestructure.PreparacionAlmacen.RepositorioCierreReposiciones.ConContextoPropio());
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IAvisadorReposiciones, Infraestructure.PreparacionAlmacen.AvisadorReposiciones>();
            // NestoAPI#645: etiquetas de bulto de las reposiciones (lectura obligatoria al recibir)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioBultosReposicion>(_ => Infraestructure.PreparacionAlmacen.RepositorioBultosReposicionSql.ConContextoPropio());
            // NestoAPI#634: diferencias de reposición (constancia, ajuste del origen y comprobación)
            _ = services.AddScoped<Infraestructure.Reposiciones.IServicioDiferenciasReposicion>(_ => new Infraestructure.Reposiciones.ServicioDiferenciasReposicion());
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IOrigenRecepcion>(sp => new Infraestructure.PreparacionAlmacen.OrigenRecepcionReposiciones(
                sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IServicioRecepcionReposiciones>(),
                sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IRepositorioCierreReposiciones>(),
                sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IAvisadorReposiciones>(),
                sp.GetRequiredService<Infraestructure.Reposiciones.IServicioDiferenciasReposicion>(),
                sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IRepositorioBultosReposicion>())
            {
                // NestoAPI#772: lo leído al recibir que siga en una furgoneta cuenta como descargado (o entregado en la tienda)
                BultosLeidosAlRecibir = Infraestructure.Agencias.RepartoPropio.RedSeguridadFurgoneta.LeidosConContextoPropio
            });
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioRecepciones, Infraestructure.PreparacionAlmacen.ServicioRecepciones>();
            // Ariadna#51 (corte 2): lo leído de una recepción en curso, en el servidor (Nesto y Ariadna ven lo mismo)
            _ = services.AddSingleton<Infraestructure.PreparacionAlmacen.IRepositorioLecturasRecepcion, Infraestructure.PreparacionAlmacen.RepositorioLecturasRecepcionSql>();
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioLecturasRecepcion, Infraestructure.PreparacionAlmacen.ServicioLecturasRecepcion>();
            // NestoAPI#762: lo que llega sin etiqueta a una reposición (se recibe solo escaneando): aviso al origen y a Compras
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IAvisadorSinEtiqueta, Infraestructure.PreparacionAlmacen.AvisadorSinEtiqueta>();
            // Revisión 30/10: «Sin etiqueta» idempotente con ClaveIdempotencia (en Modificaciones, sin script de BD)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioIdempotenciaSinEtiqueta, Infraestructure.PreparacionAlmacen.RepositorioIdempotenciaSinEtiqueta>();
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioSinEtiquetaRecepcion, Infraestructure.PreparacionAlmacen.ServicioSinEtiquetaRecepcion>();
            // Lo que ve el mozo de cada producto además del nombre: familia, subgrupo, tamaño y unidad (un solo sitio)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioFichasProducto>(_ => Infraestructure.PreparacionAlmacen.RepositorioFichasProducto.ConContextoPropio());
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IFotosProductoAlmacen, Infraestructure.PreparacionAlmacen.FotosProductoAlmacen>();
            // NestoAPI#605: varios códigos de barras por producto
            _ = services.AddScoped<Infraestructure.Productos.IRepositorioCodigosBarras>(_ => Infraestructure.Productos.RepositorioCodigosBarras.ConContextoPropio());
            _ = services.AddScoped<Infraestructure.Productos.IServicioCodigosBarras, Infraestructure.Productos.ServicioCodigosBarras>();
            // NestoAPI#762: lo repuesto en los últimos meses sin código de barras (limpieza inicial)
            _ = services.AddScoped(sp => new Infraestructure.Productos.InformeRepuestosSinCodigo(
                new Infraestructure.Productos.RepositorioRepuestosSinCodigoSql(),
                sp.GetRequiredService<Infraestructure.Productos.IRepositorioCodigosBarras>()));
            // Ariadna#47: los demás productos con stock en el hueco de cada línea (una consulta por respuesta)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioProductosMismoHueco>(_ => Infraestructure.PreparacionAlmacen.RepositorioProductosMismoHuecoSql.ConContextoPropio());
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IFichasProductoAlmacen,Infraestructure.PreparacionAlmacen.FichasProductoAlmacen>();
            // Sugerencia 824: la ficha del producto en Ariadna (botón «Producto»), con lo que ya leen los servicios de arriba
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioFichaProductoAlmacen>(sp => new Infraestructure.PreparacionAlmacen.ServicioFichaProductoAlmacen(
                sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IServicioUbicacionesAlmacen>(),
                sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IFichasProductoAlmacen>(),
                sp.GetRequiredService<Infraestructure.Productos.IServicioCodigosBarras>(),
                new Infraestructure.Kits.ProductoService()));
            // Ariadna#12: coger de otro hueco lo que no estaba en el de la parada
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioCambioHueco>(_ => Infraestructure.PreparacionAlmacen.RepositorioCambioHuecoSql.ConContextoPropio());
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioCambioHuecoPicking, Infraestructure.PreparacionAlmacen.ServicioCambioHuecoPicking>();
            // … y en la salida de una reposición (la reserva en estado 4 pasa de hueco por la puerta única de Ubicaciones)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioCambioHuecoReposicion>(_ => Infraestructure.PreparacionAlmacen.RepositorioCambioHuecoReposicionSql.ConContextoPropio());
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioCambioHuecoReposicion, Infraestructure.PreparacionAlmacen.ServicioCambioHuecoReposicion>();
            // NestoAPI#646: avisos de «ha cambiado» a las demás PDA del mismo documento (SignalR, AlmacenHub)
            _ = services.AddSingleton<Infraestructure.Notificaciones.IAvisosAlmacen, Infraestructure.Notificaciones.AvisosAlmacenSignalR>();
            // NestoAPI#768: escáner remoto (Ariadna lee, el Nesto del mismo usuario lo recibe por AvisosHub). Singleton: guarda la idempotencia
            _ = services.AddSingleton<Infraestructure.Notificaciones.IEscanerRemoto, Infraestructure.Notificaciones.ServicioEscanerRemoto>();
            // Ariadna#8: el mozo informa de un dato mal de la ficha (foto → Tienda online; lo demás → Compras)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioAvisosFicha>(_ => Infraestructure.PreparacionAlmacen.RepositorioAvisosFichaSql.ConContextoPropio());
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IAvisadorFichaProducto, Infraestructure.PreparacionAlmacen.AvisadorFichaProducto>();
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioAvisosFicha, Infraestructure.PreparacionAlmacen.ServicioAvisosFicha>();
            // Etiquetas de hueco: un solo sitio que imprime, en el servidor, para Nesto y Ariadna (05/10/26)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioEtiquetasHueco>(sp => new Infraestructure.PreparacionAlmacen.ServicioEtiquetasHueco(
                Infraestructure.PreparacionAlmacen.RepositorioEtiquetasHuecoSql.ConContextoPropio(),
                sp.GetRequiredService<ILectorParametrosUsuario>(),
                Infraestructure.PreparacionAlmacen.ImpresoraEtiquetasConAjuste.EnWindows()));
            // NestoAPI#654: etiquetas de código de barras de producto, con la ficha del almacén y la impresora de las de hueco
            // NestoAPI#762: y con los códigos de barras, para la EAN-13 (el del producto o su código interno)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioEtiquetasProducto>(sp => new Infraestructure.PreparacionAlmacen.ServicioEtiquetasProducto(
                sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IRepositorioFichasProducto>(),
                sp.GetRequiredService<ILectorParametrosUsuario>(),
                Infraestructure.PreparacionAlmacen.ImpresoraEtiquetasConAjuste.EnWindows(),
                sp.GetRequiredService<Infraestructure.Productos.IServicioCodigosBarras>()));

            // NestoAPI#595 (slice 5, Ariadna#13): la etiqueta de agencia desde el packing (DesdePedido + Tramitar + ImprimirEtiqueta
            // y, si el usuario lo tiene, facturar), con los mismos endpoints que Agencias de Nesto
            _ = services.AddScoped<Infraestructure.Agencias.IServicioEtiquetaPacking>(sp => new Infraestructure.Agencias.ServicioEtiquetaPacking(
                usuario => new Infraestructure.Agencias.PasosEtiquetaPackingApi(usuario,
                    sp.GetRequiredService<Infraestructure.PreparacionAlmacen.IServicioPreparacionAlmacen>()),
                sp.GetRequiredService<ILectorParametrosUsuario>()));

            // Ariadna#43: lecturas que no casan y por qué se teclea un producto con código («No se lee»)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IRepositorioLecturasAlmacen>(_ => Infraestructure.PreparacionAlmacen.RepositorioLecturasAlmacenSql.ConContextoPropio());
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioLecturasAlmacen, Infraestructure.PreparacionAlmacen.ServicioLecturasAlmacen>();

            // NestoAPI#672: desplazamiento por impresora de etiquetas y etiqueta de prueba (la prueba imprime sin el decorador:
            // ya lleva aplicado el ajuste que se está probando)
            _ = services.AddScoped<Infraestructure.PreparacionAlmacen.IServicioAjustesImpresoras>(sp => new Infraestructure.PreparacionAlmacen.ServicioAjustesImpresoras(
                new Infraestructure.PreparacionAlmacen.RepositorioAjustesImpresorasSql(),
                sp.GetRequiredService<ILectorParametrosUsuario>(),
                new Infraestructure.PreparacionAlmacen.ImpresoraEtiquetasWindows(),
                Infraestructure.PreparacionAlmacen.LenguajeImpresorasWindows.Compartido));

            // Ventana de facturas pendientes de Verifactu (NestoAPI#522)
            _ = services.AddScoped<Infraestructure.Verifactu.IServicioFacturasPendientesVerifactu, Infraestructure.Verifactu.ServicioFacturasPendientesVerifactu>();

            // Servicios de Informes (Nesto#340 Fase 1A)
            _ = services.AddScoped<IInformesService, InformesService>();

            // Lecturas del panel de Comisiones del cliente (Nesto#340 Fase 1B)
            _ = services.AddScoped<IComisionesLecturaService, ComisionesLecturaService>();

            // Planes de Ventajas - CRUD para el cliente (Nesto#340 Fase 1B)
            _ = services.AddScoped<IPlanesVentajasService, PlanesVentajasService>();

            // Lista de productos en alquiler para el cliente (Nesto#340 Fase 1C.1)
            _ = services.AddScoped<IProductosAlquilerService, ProductosAlquilerService>();

            // Validación de "Servir junto" (NestoAPI#161)
            _ = services.AddScoped<IServicioValidarServirJunto, ServicioValidarServirJunto>();

            // Servicios de sincronización bidireccional (External Systems <-> Nesto)
            // Push Subscription: usa SyncWebhookController
            _ = services.AddSingleton<ISyncTableHandlerBase, ClientesSyncHandler>();
            _ = services.AddSingleton<ISyncTableHandlerBase, ProductosSyncHandler>();
            _ = services.AddSingleton<ISyncTableHandlerBase, PrestashopProductosSyncHandler>();
            _ = services.AddSingleton<SyncTableRouter>(sp =>
            {
                var handlers = sp.GetServices<ISyncTableHandlerBase>();
                return new SyncTableRouter(handlers);
            });
            _ = services.AddScoped<MessageRetryManager>(sp =>
            {
                var db = new NVEntities();
                return new MessageRetryManager(db);
            });
            _ = services.AddScoped<SyncWebhookController>();

            // Registrar el contexto de la base de datos
            _ = services.AddScoped<DbContext>(_ => new ApplicationDbContext());

            // Registrar los controladores de Web API
            _ = services.AddControllersAsServices(typeof(Startup).Assembly);

            // Construir el proveedor de servicios
            return services.BuildServiceProvider();
        }

        private void ConfigureWebApi(HttpConfiguration config)
        {
            //config.MapHttpAttributeRoutes();

            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;

            JsonMediaTypeFormatter jsonFormatter = config.Formatters.OfType<JsonMediaTypeFormatter>().First();
            jsonFormatter.SerializerSettings.ContractResolver = new DefaultContractResolver();

            //Elimino que el sistema devuelva en XML, sólo trabajaremos con JSON
            _ = config.Formatters.Remove(System.Web.Http.GlobalConfiguration.Configuration.Formatters.XmlFormatter);
        }

        private void ConfigureHangfire(IAppBuilder app)
        {
#if DEBUG
            Console.WriteLine("⚠️ Hangfire deshabilitado en modo DEBUG");
            System.Diagnostics.EventLog.WriteEntry("Application",
                "Hangfire deshabilitado en modo DEBUG (desarrollo)",
                System.Diagnostics.EventLogEntryType.Information);
            return;
#endif

            try
            {
                // Obtener connection string de Web.config
                string connectionString = ConfigurationManager.ConnectionStrings["NestoConnection"].ConnectionString;

                // Configurar Hangfire para usar SQL Server
                Hangfire.GlobalConfiguration.Configuration
                    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
                    {
                        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                        QueuePollInterval = TimeSpan.Zero,
                        UseRecommendedIsolationLevel = true,
                        DisableGlobalLocks = true
                    });

                // Configurar el dashboard de Hangfire en /hangfire
                // NestoAPI#749: solo Dirección e Informática, con autenticación integrada de Windows (ver HangfireAuthorizationFilter)
                app.UseHangfireDashboard("/hangfire", new DashboardOptions
                {
                    Authorization = new[] { new HangfireAuthorizationFilter() }
                });

                // Iniciar el servidor de Hangfire
                app.UseHangfireServer(new BackgroundJobServerOptions
                {
                    WorkerCount = 1 // Solo un worker para evitar procesamiento duplicado
                });

                // NestoAPI#619: servidor aparte, de un hilo y solo para la cola «entrenamiento» (reentrenar el modelo de
                // llamadas tarda; en el servidor de arriba dejaría parados los jobs de cada 5 minutos). El de arriba solo
                // atiende la cola «default», así que no hay doble procesamiento.
                app.UseHangfireServer(new BackgroundJobServerOptions
                {
                    ServerName = Environment.MachineName + ":" + Infraestructure.Rapports.ReentrenamientoModeloLlamadasJobsService.COLA,
                    WorkerCount = 1,
                    // NestoAPI#629: también los indicadores de stock (recorren ExtractoProducto y LinPedidoVta enteras).
                    Queues = new[]
                    {
                        Infraestructure.Rapports.ReentrenamientoModeloLlamadasJobsService.COLA,
                        Infraestructure.IndicadoresStock.IndicadoresStockJobsService.COLA
                    }
                });

                // Configurar jobs recurrentes
                ConfigurarJobsRecurrentes();

                Console.WriteLine("✅ Hangfire configurado correctamente");
                System.Diagnostics.EventLog.WriteEntry("Application",
                    "Hangfire configurado correctamente en NestoAPI. Dashboard disponible en /hangfire",
                    System.Diagnostics.EventLogEntryType.Information);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error al configurar Hangfire: {ex.Message}");
                System.Diagnostics.EventLog.WriteEntry("Application",
                    $"Error al configurar Hangfire: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                    System.Diagnostics.EventLogEntryType.Error);
                throw;
            }
        }

        /// <summary>
        /// NestoAPI#404: fija el protocolo TLS del proceso UNA VEZ, al arrancar.
        ///
        /// El Web.config declara <c>&lt;httpRuntime targetFramework="4.5"&gt;</c>, lo que activa el modo
        /// de compatibilidad de .NET 4.5: ahí el valor por defecto de
        /// <c>ServicePointManager.SecurityProtocol</c> es <b>SSL 3.0 + TLS 1.0</b> (a partir de 4.6
        /// pasó a ser moderno). Y <c>ServicePointManager</c> es GLOBAL al proceso.
        ///
        /// Hasta ahora cada llamada saliente lo fijaba por su cuenta justo antes de usarlo (12
        /// sitios repartidos), menos AmazonFeedsGateway, que no lo fija nunca. Resultado: si tras
        /// un reinicio del app pool la PRIMERA llamada HTTPS saliente era la de Amazon, salía con
        /// TLS 1.0 y la SP-API la rechazaba con "No se puede crear un canal seguro SSL/TLS". En
        /// cuanto cualquier otro camino (Verifacti, Redsys, AEAT, agencias...) fijaba Tls12,
        /// quedaba arreglado para todo el proceso y Amazon volvía a funcionar solo.
        ///
        /// Caso real: despliegue del 24/08/26. El app pool arrancó a las 13:28:20 y a las 13:30:06
        /// fallaron a la vez las DOS rutas de Amazon (el botón "Subir factura" de Enrique y el job
        /// AmazonFacturasJobs); ni una sola vez antes en 45 días ni después. Es una carrera que
        /// reaparece en CADA reinicio, y en esa ventana la facturación de Amazon queda rota.
        ///
        /// Se deja Tls12 porque es exactamente lo que ya fijan los otros 12 sitios y lo que
        /// funciona hoy en producción. Los 12 pasan a ser redundantes y se pueden ir quitando.
        /// El arreglo de fondo sería subir el targetFramework a 4.8, pero eso cambia muchos
        /// comportamientos de ASP.NET y merece su propio cambio.
        /// </summary>
        /// <summary>NestoAPI#764: la hora de España peninsular (Romance Standard Time); si el servidor no la tuviera, la local.</summary>
        internal static TimeZoneInfo ZonaHorariaPeninsular()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
            }
            catch (Exception)
            {
                return TimeZoneInfo.Local;
            }
        }

        internal static void ConfigurarTlsDelProceso()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        private void ConfigurarJobsRecurrentes()
        {
            // TNV#66: avisa al cliente por push cuando su pedido cambia de estado (ha salido,
            // ha salido una parte, incidencia con la entrega, o lleva horas esperando el pago).
            // Cada media hora: el paso a albarán ocurre a lo largo del día y el seguimiento de la
            // agencia se refresca cada 30 min en horario de reparto (#516), así que más frecuencia no
            // adelantaría nada.
            // El job se registra siempre, pero solo hace algo si AvisosPedidos:Activo está a true
            // en Web.config: así se enciende sin desplegar, el día que la app esté publicada.
            RecurringJob.AddOrUpdate(
                "avisos-pedidos-cliente",
                () => Infraestructure.Notificaciones.AvisosPedidosJobsService.AvisarCambiosDeEstado(),
                "*/30 * * * *", // Cron: cada 30 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'avisos-pedidos-cliente' configurado (cada 30 minutos)");

            // Sincronización de Productos cada 5 minutos
            RecurringJob.AddOrUpdate(
                "sincronizar-productos",
                () => SincronizacionJobsService.SincronizarProductos(),
                "*/5 * * * *", // Cron: cada 5 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );

            Console.WriteLine("✅ Job recurrente 'sincronizar-productos' configurado (cada 5 minutos)");

            // NestoAPI#410: a las 2:00, encolar en Nesto_sync los productos con movimientos de
            // stock de los últimos 2 días, para que los stocks de Odoo y PrestaShop no se desvíen.
            // Solo encola: la publicación la hace 'sincronizar-productos' en sus pasadas.
            // Si se pasa a cadencia horaria, cambiar el cron Y la ventana a la vez (ver el
            // comentario de HORAS_VENTANA_NOCTURNA).
            RecurringJob.AddOrUpdate(
                "sincronizar-stocks-nocturno",
                () => Infraestructure.Sincronizacion.SincronizacionStocksJobsService.EncolarProductosConMovimientos(
                    Infraestructure.Sincronizacion.SincronizacionStocksJobsService.HORAS_VENTANA_NOCTURNA),
                "0 2 * * *", // Cron: todos los días a las 2:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'sincronizar-stocks-nocturno' configurado (diario a las 2:00)");

            // NestoAPI#406: marca con el sentinel -1 los productos de las familias con
            // público = profesional. A la 1:30, ANTES del job de stocks: si marca algo, lo encola,
            // y así la republicación entra en la misma tanda nocturna en vez de esperar un día.
            RecurringJob.AddOrUpdate(
                "sentinel-precio-publico",
                () => Infraestructure.Sincronizacion.SentinelPrecioPublicoJobsService.MarcarProductosDeFamiliasConPublicoIgual(),
                "30 1 * * *", // Cron: todos los días a la 1:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'sentinel-precio-publico' configurado (diario a la 1:30)");

            // NestoAPI#423: encola los productos cuyas campañas empiezan o terminan. A la 1:00,
            // ANTES del sentinel y del job de stocks, por la misma razón que aquellos: lo que
            // encole entra en la tanda nocturna y la tienda amanece con los precios del día.
            // Es imprescindible porque una campaña que caduca por fecha NO modifica ninguna fila,
            // así que ningún detector por [Fecha Modificación] puede verla.
            RecurringJob.AddOrUpdate(
                "vigencia-campanas",
                () => Infraestructure.Sincronizacion.VigenciaCampanasJobsService.EncolarProductosConCampanasQueCambian(
                    Infraestructure.Sincronizacion.VigenciaCampanasJobsService.DIAS_VENTANA),
                "0 1 * * *", // Cron: todos los días a la 1:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'vigencia-campanas' configurado (diario a la 1:00)");

            // Job de correos post-compra: se ejecuta los jueves a las 05:00 (hasta el 17/09/26 iba los
            // miércoles a las 20:30; la consulta de albaranes tarda ~50 s y a esa hora aún hay gente
            // trabajando). La ventana de albaranes (jueves a miércoles) y el envío (sábado 10:00) no
            // dependen de la hora del job: los calcula CorreosPostCompraJobsService.CalcularVentana.
            // Issue #74: Sistema de correos automáticos con videos personalizados post-compra
            RecurringJob.RemoveIfExists("correos-postcompra-procesar-albaranes"); // Eliminar job viejo (diario)
            RecurringJob.AddOrUpdate(
                "correos-postcompra-semanal",
                () => CorreosPostCompraJobsService.ProcesarCorreosSemanales(),
                "0 5 * * 4", // Cron: jueves a las 05:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );

            Console.WriteLine("✅ Job recurrente 'correos-postcompra-semanal' configurado (jueves a las 05:00)");

            // NestoAPI#532: recordatorio de reposición con las ventas de Nesto (todos los canales). Jueves a las 9:00
            // (hasta el 22/10/26 iba a las 05:30, cuando solo era sombra): en Activo el correo al cliente tiene que llegar
            // por la mañana. El cálculo son unos segundos (consulta medida el 25/09/26: ~3 s, ~41.000 lecturas lógicas, con
            // NOLOCK y por índice: no molesta en horario); lo que más tarda son los envíos, uno cada 2,5 s (límite de
            // Office 365) más el enlace y la foto de cada producto. NO HACE NADA salvo con el parámetro RecordatorioReposicion
            // de (defecto) a "Sombra" (solo a Informática) o "Activo" (Scripts/Issue532_RecordatorioReposicionActivo.sql).
            RecurringJob.AddOrUpdate(
                "recordatorio-reposicion-semanal",
                () => RecordatorioReposicionJobsService.Procesar(),
                "0 9 * * 4", // Cron: jueves a las 09:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'recordatorio-reposicion-semanal' configurado (jueves a las 09:00; apagado salvo parámetro)");

            // NestoAPI#662: el mismo cálculo por push a los clientes de NestoTiendas. Diario de lunes a jueves a las 9:30
            // (la regla de una push cada 7 días evita repetir). Interruptor PROPIO: parámetro RecordatorioReposicionPush
            // de (defecto) (Apagado / Sombra / Activo; sin fila = apagado). Scripts/Issue662_RecordatorioReposicionPush.sql.
            RecurringJob.AddOrUpdate(
                "recordatorio-reposicion-push-diario",
                () => RecordatorioReposicionPushJobsService.Procesar(),
                "30 9 * * 1-4", // Cron: de lunes a jueves a las 09:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'recordatorio-reposicion-push-diario' configurado (L-J a las 09:30; apagado salvo parámetro)");

            // Issue #137: Informe semanal de clientes nuevos por vendedor
            RecurringJob.AddOrUpdate(
                "informe-clientes-nuevos-semanal",
                () => InformeClientesNuevosJobsService.ProcesarInformeSemanal(),
                "0 9 * * 5", // Cron: viernes a las 9:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'informe-clientes-nuevos-semanal' configurado (viernes a las 9:00)");

            // NestoAPI#225: rotación automática del client_secret LWA de Amazon SP-API.
            // Diario a las 7:00: sondea la cola SQS (persiste secretos nuevos) y rota si el
            // secreto almacenado está cerca de caducar. No hace nada hasta ~15 días antes de caducar.
            RecurringJob.AddOrUpdate(
                "amazon-rotacion-credenciales",
                () => AmazonCredencialRotacionJobsService.ProcesarRotacionCredenciales(),
                "0 7 * * *", // Cron: todos los días a las 7:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'amazon-rotacion-credenciales' configurado (diario a las 7:00)");

            // Diario a las 6:30: registra en ComparativaAgenciaSombra qué envíos reales habría ganado
            // cada agencia SOMBRA (p.ej. CTT) y a qué coste, para evaluar si negociar con ella. No
            // hace nada si no hay agencias sombra (AgenciasTransporte.EsSombra = 1).
            RecurringJob.AddOrUpdate(
                "comparativa-agencia-sombra",
                () => ComparativaAgenciaSombraJobsService.ProcesarComparativaDiaria(),
                "30 6 * * *", // Cron: todos los días a las 6:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'comparativa-agencia-sombra' configurado (diario a las 6:30)");

            // NestoAPI#552 (fase 1, sombra): diario a las 6:45 compara las peticiones a GLS de AYER que mandó Nesto
            // con las que construiría la API. Solo lee; solo escribe en ELMAH si alguna no coincide.
            RecurringJob.AddOrUpdate(
                "sombra-peticiones-gls",
                () => Infraestructure.Agencias.Gls.SombraPeticionesGls.ProcesarSombraDiaria(),
                "45 6 * * *",
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'sombra-peticiones-gls' configurado (diario a las 6:45)");

            // NestoAPI#547 (corte b): SOMBRA de los precios medios. Domingo a las 6:30, después del SP «Precios
            // Medios» de msdb (00:30): calcula la media en C# y la compara al diezmilésimo con lo que ha dejado el SP.
            // Solo lee (lo único que escribe es la tabla de diagnóstico PreciosMediosSombra). NO HACE NADA salvo con
            // el parámetro PreciosMediosSombra de (defecto) a "1"; si el SP sigue corriendo, se aplaza sin comparar.
            RecurringJob.AddOrUpdate(
                "precios-medios-sombra",
                () => Infraestructure.PreciosMedios.PreciosMediosJobsService.ProcesarSombraSemanal(),
                "30 6 * * 0", // Cron: domingo a las 6:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'precios-medios-sombra' configurado (domingo a las 6:30; apagado salvo parámetro)");

            // NestoAPI#547 (corte c): INCREMENTAL de precios medios. Todos los días a las 2:30 recalcula (y ESCRIBE) la
            // media de los productos con compras modificadas desde la última pasada y de los que tienen movimientos de
            // stock con fecha pasada. NO HACE NADA salvo con PreciosMedios:EscribirIncremental = true en Web.config; si
            // el SP del domingo sigue corriendo, se aplaza.
            RecurringJob.AddOrUpdate(
                "precios-medios-incremental",
                () => Infraestructure.PreciosMedios.PreciosMediosIncrementalJobsService.ProcesarPasadaNocturna(),
                "30 2 * * *", // Cron: todos los días a las 2:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'precios-medios-incremental' configurado (diario a las 2:30; apagado salvo Web.config)");

            // Poll de seguimiento de envíos (#248). Actualiza Estado (Entregado/Incidentado), el último
            // texto de la agencia (DetalleEstado, de donde sale «en reparto», #516) y FechaEntrega real
            // consultando a cada agencia con gestión remota. Acotado a los envíos desde una fecha de corte
            // fija (SeguimientoEnviosJobsService.FECHA_CORTE), así que no recorre el histórico antiguo de GLS.
            // NestoAPI#516: cada 30 min en horario de reparto (lun-sáb, 8:00-20:00) y cada 2 horas el resto
            // (a las horas pares en punto, como antes). El cron dispara cada 30 min y el propio job
            // descarta las pasadas de fuera de horario (SeguimientoEnviosJobsService.TocaPasadaProgramada).
            RecurringJob.AddOrUpdate(
                "seguimiento-envios",
                () => SeguimientoEnviosJobsService.ProcesarSeguimientosAsync(),
                SeguimientoEnviosJobsService.CRON_SEGUIMIENTO, // Cron: cada 30 min (filtrado por horario en el job)
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'seguimiento-envios' configurado (cada 30 min en horario de reparto, cada 2 horas el resto)");

            // NestoAPI#767: de noche, coordenadas (Nominatim, 1 petición/s) de las paradas en reparto y de los clientes activos
            // de las rutas propias, con tope por noche. De día solo se geocodifica bajo demanda (lo que pida la lista del reparto).
            RecurringJob.AddOrUpdate(
                Infraestructure.Agencias.RepartoPropio.GeocodificacionRepartoJobsService.JOB_NOCTURNO,
                () => Infraestructure.Agencias.RepartoPropio.GeocodificacionRepartoJobsService.GeocodificarClientesRutasPropias(),
                "40 3 * * *", // Cron: todos los días a las 3:40
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );

            // NestoAPI#329: cada hora (a y cuarto, para no coincidir con seguimiento-envios),
            // consulta estados Verifactu pendientes, reintenta las facturas sin declarar (series
            // que tramitan, desde la fecha de arranque de la sombra) y marca fichas con NIF
            // rechazado. No-op si Verifacti:Habilitado está apagado.
            // NestoAPI#346: cadencia HORARIA porque el art. 16.4 de la Orden HAC/1177/2024 exige
            // reintentar la remisión pendiente "al menos una vez cada hora". El ruido de
            // reintentos repetidos lo corta DeduplicadorErroresVerifactu.
            RecurringJob.AddOrUpdate(
                "verifactu-estados",
                () => Infraestructure.Verifactu.VerifactuJobsService.Procesar(),
                "15 * * * *", // Cron: cada hora, a y cuarto
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'verifactu-estados' configurado (cada hora)");

            // NestoAPI#522: de lunes a viernes a las 8:30, nota en la campana de Nesto al grupo Administración
            // si hay facturas pendientes de Verifactu o incorrectas en la AEAT (al pulsarla se abre la ventana
            // para verlas y reintentarlas). Sin pendientes no avisa. Interruptor: Verifactu:AvisoDiarioPendientes.
            // NestoAPI#772: de lunes a viernes a las 22:00, los repartidores con bultos en la furgoneta y sin «Terminar jornada»
            // hoy: aviso en la campana de Nesto a Almacén, Dirección y al repartidor (el mismo que al terminar con bultos).
            RecurringJob.AddOrUpdate(
                Infraestructure.Agencias.RepartoPropio.FurgonetaSinDescargarJobsService.JOB_ID,
                () => Infraestructure.Agencias.RepartoPropio.FurgonetaSinDescargarJobsService.Avisar(),
                Infraestructure.Agencias.RepartoPropio.FurgonetaSinDescargarJobsService.CRON, // Cron: de lunes a viernes a las 22:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'furgoneta-sin-descargar' configurado (L-V a las 22:00)");

            RecurringJob.AddOrUpdate(
                "verifactu-aviso-pendientes",
                () => Infraestructure.Verifactu.AvisoFacturasPendientesVerifactuJobsService.Avisar(),
                "30 8 * * 1-5", // Cron: de lunes a viernes a las 8:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'verifactu-aviso-pendientes' configurado (laborables a las 8:30)");

            // NestoAPI#366: cada 30 minutos, cierra el bucle de las facturas subidas a Amazon
            // (feed UPLOAD_VAT_INVOICE): consulta getFeed de las filas ENVIADA y guarda el
            // resultado (DONE/FATAL) con su informe. No-op si no hay filas pendientes.
            RecurringJob.AddOrUpdate(
                "amazon-facturas-resultados",
                () => Infraestructure.CanalesExternos.Amazon.AmazonFacturasJobsService.ComprobarResultadosFeeds(),
                "*/30 * * * *", // Cron: cada 30 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'amazon-facturas-resultados' configurado (cada 30 minutos)");

            // NestoAPI#361: picking de CIERRE del día. Venía de una tarea del Task Scheduler que
            // llamaba a la API por HTTP y que estaba programada a las 10:59:40 para no pasarse de
            // las 11h, porque el horizonte de entrega se deducía del reloj y arrancar un segundo
            // tarde hacía que el picking sacara TAMBIÉN las entregas de mañana. Resuelto eso (el
            // horizonte se declara, no se deduce), llegar unos segundos tarde dejó de costar nada
            // y el job se puede programar a las 11:00 en punto.
            //
            // De hecho el retraso natural de Hangfire (unos segundos) ahora JUEGA A FAVOR: da
            // margen a que commiteen los pedidos metidos justo antes del corte. El último pedido
            // que entra es el de las 10:59:59; a las 11:00:00 ya se le asigna la siguiente ruta.
            // NestoAPI#416: se registra el punto de entrada VOID. El que devuelve la lista es
            // solo para el endpoint manual: Hangfire serializa el retorno dentro del commit del
            // Succeeded y la lista entera de pedidos lo reventaba (job en Failed con el picking
            // ya sacado).
            RecurringJob.AddOrUpdate(
                "picking-cierre-diario",
                () => PickingJobsService.SacarPickingDeCierreJob(),
                // NestoAPI#577: el cron no puede leer el parámetro «(defecto)» HoraCortePicking (Models/Picking/HoraCortePicking.cs).
                // Si se cambia la hora de corte en ese parámetro, hay que cambiar también esta hora a mano.
                "0 11 * * 1-5", // Cron: de lunes a viernes a las 11:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );

            Console.WriteLine("✅ Job recurrente 'picking-cierre-diario' configurado (L-V a las 11:00)");

            // NestoAPI#764: picking automático de las rutas internas (AT/16, las de RutasRepartoPropio) a las 9:00, con la forma
            // del de cierre de arriba. De los pedidos con día de reparto por código postal, solo los que tocan hoy. Nace APAGADO:
            // interruptor PickingAutomaticoRutasInternas = "1" bajo «(defecto)» (Scripts/Issue764_PickingRutasInternas.sql);
            // solo laborables de Algete. El cron sale de la hora de corte de las rutas internas (CalendarioRutasDia.HORA_CORTE).
            RecurringJob.AddOrUpdate(
                Infraestructure.Picking.PickingRutasInternasJobsService.JOB_ID,
                () => Infraestructure.Picking.PickingRutasInternasJobsService.SacarPickingRutasInternasJob(),
                Infraestructure.PedidosVenta.CalendarioRutasDia.CRON_PICKING, // Cron: de lunes a viernes a las 9:00
                new RecurringJobOptions
                {
                    TimeZone = ZonaHorariaPeninsular()
                }
            );
            Console.WriteLine("✅ Job recurrente 'picking-rutas-internas' configurado (L-V a las 9:00, apagado salvo interruptor)");

            // NestoAPI#577 (corte 3b): las reposiciones se rellenan solas a la hora de cierre de su fila del calendario
            // (ReposicionesCalendario, lo mantiene Almacén). Como el picking de cierre (#361), el corte es un DATO: cada
            // pasada mira qué reposiciones que cierran o llegan HOY tienen ya su día de cierre + HoraCierre en el pasado
            // (corte 3d: Algete → tienda cierra el laborable anterior a las 13:00) y aún no están hechas
            // (ReposicionesTraspasos: Herramienta 'Automatico' + FechaCorte) y las rellena con ESE instante, no con el
            // reloj. Por eso basta un cron frecuente y llegar unos segundos o minutos tarde no cambia nada. Las horas de
            // cierre del calendario tienen que caer dentro del cron (ReposicionesAutomaticasJobsService.CRON).
            RecurringJob.AddOrUpdate(
                "reposiciones-automaticas",
                () => Infraestructure.Reposiciones.ReposicionesAutomaticasJobsService.RellenarPendientes(),
                Infraestructure.Reposiciones.ReposicionesAutomaticasJobsService.CRON, // Cron: cada 5 min, L-V de 6:00 a 21:55
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'reposiciones-automaticas' configurado (cada 5 min, L-V de 6:00 a 21:55)");

            // NestoAPI#402: el reindexado del buscador, que antes dependía de una tarea fuera del
            // repo. A las 20:30 porque prdActualizarClasificacionProductos (más vendidos) corre a
            // las 20:00 y tarda unos 5 minutos, y el índice guarda esa posición para ponderar.
            RecurringJob.AddOrUpdate(
                "reindexar-buscador",
                () => Infraestructure.Buscador.BuscadorJobsService.Reindexar(),
                "30 20 * * *", // Cron: todos los días a las 20:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'reindexar-buscador' configurado (diario a las 20:30)");

            // TNV#68: cobrar antes de crear el pedido quita el pedido fantasma, pero deja el hueco
            // contrario: el cliente paga y cierra la app antes de que se cree el pedido. Este job
            // devuelve esos cobros y avisa de los que se quedaron a medio aplicar. Cada hora, que
            // es de sobra: lo normal es que no encuentre nada.
            RecurringJob.AddOrUpdate(
                "cobros-carrito-huerfanos",
                () => Infraestructure.Pagos.CobrosCarritoJobsService.RevisarCobrosHuerfanos(),
                "5 * * * *", // Cron: cada hora, en el minuto 5
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'cobros-carrito-huerfanos' configurado (cada hora)");

            // NestoAPI#663: Aplázame exige capturar el cobro al enviar (como mucho 15 días después de confirmarlo). Cada
            // hora en horario laboral se capturan los cobros cuyo pedido ya está entero en albarán o factura, y se avisa a
            // Informática de los que lleven 12 días sin poder capturarse. Sin cobros de Aplázame no hace nada.
            RecurringJob.AddOrUpdate(
                "aplazame-capturas",
                () => Infraestructure.Pagos.Aplazame.AplazameJobsService.CapturarCobrosServidos(),
                "20 8-20 * * 1-6", // Cron: de lunes a sábado, cada hora de 8:20 a 20:20
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'aplazame-capturas' configurado (cada hora en horario laboral)");

            // Ariadna#8: un aviso de dato mal se cierra solo («Cambiado») cuando el dato ya no es el que había al avisar.
            // Cada hora es de sobra: lo normal es que lo cierren a mano con el botón del correo.
            RecurringJob.AddOrUpdate(
                "avisos-ficha-producto",
                () => Infraestructure.PreparacionAlmacen.AvisosFichaProductoJobsService.RevisarCambios(),
                "25 * * * *", // Cron: cada hora, en el minuto 25
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'avisos-ficha-producto' configurado (cada hora)");

            // NestoAPI#402 (16/09/26): la sincronización de clientes pasa a Hangfire. Hasta hoy la
            // lanzaba la tarea «Nesto_sync Clientes» del Task Scheduler de RDS2016 (cada 5 minutos,
            // GET api/Clientes/Sync), que se deshabilita al publicar esta versión. Hace exactamente lo
            // mismo (GestorSincronizacion.ProcesarTabla "Clientes"), pero aquí se ve en el dashboard
            // y deja rastro si falla.
            RecurringJob.AddOrUpdate(
                "sincronizar-clientes",
                () => SincronizacionJobsService.SincronizarClientes(),
                "*/5 * * * *", // Cron: cada 5 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'sincronizar-clientes' configurado (cada 5 minutos)");

            // NestoAPI#498: encola los clientes con primer presupuesto o primer pedido nuevos, para
            // que Odoo mueva sus leads el mismo día. Solo encola: publica 'sincronizar-clientes'.
            // Ventana de 90 minutos para una cadencia de 30 (si se cambia el cron, cambiar también
            // MINUTOS_VENTANA_FRECUENTE). Al minuto 7 y 37, para no coincidir con los de :00 y :30.
            RecurringJob.AddOrUpdate(
                "fechas-compras-clientes",
                () => Infraestructure.Sincronizacion.FechasComprasClientesJobsService.EncolarClientesConPrimerasCompras(
                    Infraestructure.Sincronizacion.FechasComprasClientesJobsService.MINUTOS_VENTANA_FRECUENTE),
                "7,37 * * * *", // Cron: cada 30 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'fechas-compras-clientes' configurado (cada 30 minutos)");

            // NestoAPI#498: de noche, además, los clientes con último pedido nuevo (FechaUltimoPedido).
            // A las 3:00, después de la tanda de productos de la 1:00 a las 2:00.
            RecurringJob.AddOrUpdate(
                "fechas-compras-clientes-nocturno",
                () => Infraestructure.Sincronizacion.FechasComprasClientesJobsService.EncolarClientesConUltimoPedido(
                    Infraestructure.Sincronizacion.FechasComprasClientesJobsService.HORAS_VENTANA_NOCTURNA),
                "0 3 * * *", // Cron: todos los días a las 3:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'fechas-compras-clientes-nocturno' configurado (diario a las 3:00)");

            // NestoAPI#534: aviso de facturas vencidas por transferencia. De lunes a viernes a las
            // 7:45: después del poll de seguimiento de las 6:00 (el gating de entrega mira el estado
            // de los envíos) y antes de que administración empiece la jornada y la remesa. El job se
            // registra siempre pero NO HACE NADA salvo que el parámetro AvisoFacturasVencidas de
            // (defecto) valga "Sombra" (la lista va solo a administración) o "Activo" (NestoAPI#544:
            // escribe a los clientes, registra en AvisosFacturasVencidas y resume a administración).
            RecurringJob.AddOrUpdate(
                "aviso-facturas-vencidas",
                () => Infraestructure.Cobros.AvisosFacturasVencidasJobsService.Procesar(),
                "45 7 * * 1-5", // Cron: de lunes a viernes a las 7:45
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'aviso-facturas-vencidas' configurado (L-V a las 7:45; apagado salvo parámetro)");

            // NestoAPI#591: correo a administración el día del evento con las señales liberadas y las que llevan
            // 15 días sin compra. El lunes recoge también lo del fin de semana. Si no hay nada nuevo, no manda nada.
            RecurringJob.AddOrUpdate(
                "senales-eventos",
                () => Infraestructure.Eventos.SenalesEventosJobsService.Procesar(),
                "50 7 * * 1-5", // Cron: de lunes a viernes a las 7:50
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'senales-eventos' configurado (L-V a las 7:50)");

            // NestoAPI#611: las inscripciones a eventos sin pagar caducan (aviso a quien las creó) y, si al día siguiente del
            // evento nadie ha pasado lista, se da a todos por asistidos y se avisa a la tienda. Todos los días: hay eventos en sábado.
            RecurringJob.AddOrUpdate(
                "inscripciones-eventos",
                () => Infraestructure.Eventos.InscripcionesEventosJobsService.Procesar(),
                "55 7 * * *", // Cron: todos los días a las 7:55
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'inscripciones-eventos' configurado (todos los días a las 7:55)");

            // NestoAPI#690: recordatorio de los eventos dos días antes, a las 10:00 (push a la app del cliente y correo con
            // «Confirmo asistencia» / «No podré ir»). Nunca fuera de 9:00-21:00. Detrás de Eventos:RecordatorioAsistencia (apagado).
            // Carlos (22/10/26): en la misma pasada, el aviso de las reservas sin pagar y su liberación al vencer el plazo del
            // evento (DiasPagoAntesEvento), detrás de Eventos:LiberarReservasSinPagar (apagado).
            RecurringJob.AddOrUpdate(
                "recordatorio-eventos",
                () => Infraestructure.Eventos.RecordatorioEventosJobsService.Procesar(),
                Infraestructure.Eventos.RecordatorioEventosJobsService.CRON, // Cron: todos los días a las 10:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'recordatorio-eventos' configurado (todos los días a las 10:00; apagado salvo interruptor)");

            // NestoAPI#746: reservas sin pagar con el evento lleno. La pasada de cada evento se encola al llenarse y se programa
            // al vencer cada plazo; esta diaria (10:05) solo recoge lo que se haya escapado. Sin el script de las columnas, o
            // con EventosPlazasLlenasActivo = 0 en «(defecto)», no hace nada.
            RecurringJob.AddOrUpdate(
                "plazas-llenas-eventos",
                () => Infraestructure.Eventos.PlazasLlenasEventosJobsService.Procesar(),
                Infraestructure.Eventos.PlazasLlenasEventosJobsService.CRON, // Cron: todos los días a las 10:05
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'plazas-llenas-eventos' configurado (todos los días a las 10:05)");

            // NestoAPI#614: el espejo de los cupones en PrestaShop (cart_rules). Con Cupones:EspejoPrestashop apagado no hace nada.
            RecurringJob.AddOrUpdate(
                "cupones-prestashop",
                () => Infraestructure.Cupones.CuponesPrestashopJobsService.Procesar(),
                Infraestructure.Cupones.CuponesPrestashopJobsService.CRON, // Cron: cada 15 minutos
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'cupones-prestashop' configurado (cada 15 minutos)");

            // NestoAPI#606 (decisión de Carlos, 08/10/26): correo a Compras con los pedidos a proveedor enviados, sin recibir y con
            // la fecha prevista ya pasada, primero los que tienen clientes esperando. Se calcula al mandarlo: lo que ya han
            // actualizado no sale. Sin nada vencido, no manda nada.
            RecurringJob.AddOrUpdate(
                "proveedores-fecha-vencida",
                () => Infraestructure.PedidosCompra.ProveedoresVencidosJobsService.Procesar(),
                Infraestructure.PedidosCompra.ProveedoresVencidosJobsService.CRON, // Cron: de lunes a viernes a las 8:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'proveedores-fecha-vencida' configurado (L-V a las 8:00)");

            // NestoAPI#603 (corte 5): aviso en la campana de Nesto a los vendedores de SugerenciasContactoAvisarA que en 7
            // días laborables no han atendido ninguna sugerencia de contacto, con los clientes que les esperan. Como mucho
            // uno por semana a cada uno; a quien usa la lista, nada. Se prueba antes con POST .../Recordar?soloListar=true.
            RecurringJob.AddOrUpdate(
                "recordatorio-sugerencias-contacto",
                () => Infraestructure.Rapports.RecordatorioSugerenciasContactoJobsService.Procesar(),
                "30 9 * * 1-5", // Cron: de lunes a viernes a las 9:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'recordatorio-sugerencias-contacto' configurado (L-V a las 9:30)");

            // Recordatorios de respuesta en Novedades (Carlos, 16/10/26): a las sugerencias e incidencias en las que el
            // asistente le ha preguntado algo al autor (POST .../Comentarios/Asistente con EsperaRespuesta) y que llevan 2
            // días laborables sin respuesta, un recordatorio del asistente con la @mención del autor; tras el 2.º, se dan
            // por cerradas (estado Cerrada, que el autor sigue viendo y reabre comentando). Solo con el interruptor
            // Novedades:RecordatoriosRespuesta a true; ensayo con POST api/Novedades/RecordatoriosRespuesta?ensayo=true.
            RecurringJob.AddOrUpdate(
                Infraestructure.Novedades.RecordatoriosRespuestaNovedadesJobsService.ID_JOB,
                () => Infraestructure.Novedades.RecordatoriosRespuestaNovedadesJobsService.Procesar(),
                Infraestructure.Novedades.RecordatoriosRespuestaNovedadesJobsService.CRON, // Cron: de lunes a viernes a las 9:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'novedades-recordatorios-respuesta' configurado (L-V a las 9:00)");

            // NestoAPI#688: avisa (buzón + campana a quien hizo el pedido, push al vendedor) cuando TODO lo pendiente de una nota
            // de entrega automática sin fecha ya tiene stock. Cada hora en horario laboral: cubre cualquier entrada de stock
            // (también la del Nesto viejo) sin sondeo en los clientes. Sin el script Issue688 no hace nada.
            RecurringJob.AddOrUpdate(
                Infraestructure.NotasEntrega.AvisoPendienteCompletoJobsService.ID_JOB,
                () => Infraestructure.NotasEntrega.AvisoPendienteCompletoJobsService.Procesar(),
                Infraestructure.NotasEntrega.AvisoPendienteCompletoJobsService.CRON, // Cron: L-V cada hora de 8:00 a 19:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'aviso-pendiente-completo' configurado (L-V cada hora de 8:00 a 19:00)");

            // NestoAPI#593: cheques regalo de las facturas hechas fuera de la API (Nesto viejo, facturación
            // agrupada…). La API ya los genera al facturar; esto recoge lo que se le escape. Todos los días a
            // las 21:30, con la facturación del día cerrada. No hace nada salvo ChequesRegalo:Generar = true y
            // una campaña Activa en su ventana (más 7 días).
            RecurringJob.AddOrUpdate(
                "cheques-regalo-reconciliacion",
                () => Infraestructure.ChequesRegalo.ChequesRegaloJobsService.Reconciliar(),
                "30 21 * * *", // Cron: todos los días a las 21:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'cheques-regalo-reconciliacion' configurado (diario a las 21:30; apagado salvo interruptor)");

            // NestoAPI#593 (TNV): push a las clientas con la app de la tienda: la del aviso que falte (cheques de la
            // reconciliación de la noche, o de quien ha instalado la app después) y el recordatorio, una sola vez, cuando
            // faltan DiasRecordatorioPush días (5) para CanjeHasta y el cheque sigue sin usar. Por la mañana, no de noche.
            // Ligero (dos consultas y unas pocas push): el servidor principal tiene un solo hilo.
            RecurringJob.AddOrUpdate(
                Infraestructure.ChequesRegalo.ChequesRegaloPushJobsService.ID_JOB,
                () => Infraestructure.ChequesRegalo.ChequesRegaloPushJobsService.EnviarPushDelDia(),
                Infraestructure.ChequesRegalo.ChequesRegaloPushJobsService.CRON, // Cron: todos los días a las 10:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'cheques-regalo-push' configurado (diario a las 10:00; solo con campaña activa)");

            // NestoAPI#619: reentrenamiento mensual del modelo de llamadas de Rapports (antes, tarea del Task Scheduler + consola
            // ModeloLlamadaPedido + commit del zip). Primer sábado de cada mes a las 02:30: el cron no sabe de «primer sábado»,
            // así que corre todos los sábados y el job sale si el día pasa de 7. No el domingo: de madrugada corre 5 horas el SP
            // de precios medios, que bloquea LinPedidoVta. Solo promueve si el modelo nuevo pasa la puerta de calidad; avisa
            // en la campana de Nesto. Va en la cola «entrenamiento», con su propio servidor de un hilo (arriba).
            RecurringJob.AddOrUpdate(
                Infraestructure.Rapports.ReentrenamientoModeloLlamadasJobsService.ID_JOB,
                () => Infraestructure.Rapports.ReentrenamientoModeloLlamadasJobsService.ReentrenarProgramado(),
                Infraestructure.Rapports.ReentrenamientoModeloLlamadasJobsService.CRON, // Cron: sábados a las 2:30 (solo el primero del mes)
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'reentrenar-modelo-llamadas' configurado (primer sábado de cada mes a las 2:30)");

            // NestoAPI#671 (corte 2d): entrenamiento mensual del modelo de sugerencias de productos. SEGUNDO sábado de cada mes a
            // las 02:30 (el primero es el de llamadas): corre todos los sábados y el job sale si no es del 8 al 14. Misma cola
            // «entrenamiento» (servidor de un hilo, arriba). Guarda zip, json, README e informe con la fecha en
            // ModeloSugerencias:Carpeta, NO activa nada y avisa en la campana de Nesto.
            RecurringJob.AddOrUpdate(
                Infraestructure.SugerenciasProductos.ReentrenamientoModeloSugerenciasJobsService.ID_JOB,
                () => Infraestructure.SugerenciasProductos.ReentrenamientoModeloSugerenciasJobsService.EntrenarProgramado(),
                Infraestructure.SugerenciasProductos.ReentrenamientoModeloSugerenciasJobsService.CRON, // Cron: sábados a las 2:30 (solo el segundo del mes)
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'entrenar-modelo-sugerencias' configurado (segundo sábado de cada mes a las 2:30)");

            // NestoAPI#671 (corte 2e): reentrenamiento SEMANAL del modelo 2 de sugerencias (aceptación, con las respuestas ✓/✗).
            // Domingos a las 03:30, misma cola «entrenamiento» (un hilo: no coincide con los de los sábados). Con menos de 1.000
            // respuestas, 150 «Añadido» o 3 vendedores no entrena (solo deja el informe). No cambia lo que ven los vendedores salvo
            // con ModeloSugerencias:RankingAceptacion = Activo en Web.config.
            RecurringJob.AddOrUpdate(
                Infraestructure.SugerenciasProductos.ReentrenamientoModeloAceptacionJobsService.ID_JOB,
                () => Infraestructure.SugerenciasProductos.ReentrenamientoModeloAceptacionJobsService.EntrenarProgramado(),
                Infraestructure.SugerenciasProductos.ReentrenamientoModeloAceptacionJobsService.CRON, // Cron: domingos a las 3:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'entrenar-modelo-aceptacion-sugerencias' configurado (domingos a las 3:30)");

            // NestoAPI#629 (corte 1 de #628, control de stock obsoleto): el día 1 a las 03:30 rellena la tabla IndicadoresStock con
            // el mes anterior (valor de stock, rotación, cobertura, días en servir…). Si el día 1 es domingo, el día 2 (de madrugada
            // del domingo corre el SP de precios medios). A las 03:30 ya han acabado los jobs de la 1:00 a las 3:00. Va en la cola
            // «indicadores» (servidor de un hilo de los jobs largos, arriba). La reconstrucción de 36 meses se encola a mano con
            // POST api/IndicadoresStock/Reconstruir?meses=36 y solo trabaja de noche.
            RecurringJob.AddOrUpdate(
                Infraestructure.IndicadoresStock.IndicadoresStockJobsService.ID_JOB_MENSUAL,
                () => Infraestructure.IndicadoresStock.IndicadoresStockJobsService.Mensual(),
                Infraestructure.IndicadoresStock.IndicadoresStockJobsService.CRON_MENSUAL, // Cron: días 1 y 2 a las 03:30 (el 2 solo si el 1 fue domingo)
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'indicadores-stock-mensual' configurado (día 1 a las 03:30; el 2 si el 1 es domingo)");

            // NestoAPI#630 (stock obsoleto, corte 2): propuestas de máximo y mínimo por producto con su explicación, guardadas en
            // PropuestasStock y en un Excel por correo (PropuestasStock:Destinatarios). Día 1 a las 04:30, una hora después del
            // 'indicadores-stock-mensual' del corte 1 (03:30). Lee 5 años de venta y extracto de todos los productos: va en la
            // cola «entrenamiento» (su servidor de un hilo, arriba) para no parar los jobs de cada 5 minutos.
            RecurringJob.AddOrUpdate(
                Infraestructure.Stocks.Tendencias.PropuestasStockJobsService.ID_JOB,
                () => Infraestructure.Stocks.Tendencias.PropuestasStockJobsService.GenerarProgramado(),
                Infraestructure.Stocks.Tendencias.PropuestasStockJobsService.CRON, // Cron: día 1 de cada mes a las 4:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'propuestas-stock-mensual' configurado (día 1 de cada mes a las 4:30)");

            // NestoAPI#643: cuadre nocturno del Outlet de la tienda (estado 4 + en la tienda = categoría Outlet). A las 04:00, después
            // de los jobs de la 1:00 a las 3:00; lo que encola en Nesto_sync lo publica la pasada de los 5 minutos de madrugada. Con
            // Outlet:SincronizarConEstado apagado (por defecto) solo simula y manda el Excel a Carlos.
            RecurringJob.AddOrUpdate(
                Infraestructure.Outlet.OutletJobsService.ID_JOB,
                () => Infraestructure.Outlet.OutletJobsService.CuadrarProgramado(),
                Infraestructure.Outlet.OutletJobsService.CRON, // Cron: todos los días a las 04:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'outlet-cuadre-nocturno' configurado (diario a las 04:00; solo simula salvo Web.config)");

            // NestoAPI#674: informe semanal a Compras de los códigos de barras de los productos activos (sin código, código que no
            // es un GTIN válido, código compartido; y, cuando llegue Ariadna#43, los que no se leen y los tecleados). Los lunes a
            // las 8:10, después del de proveedores vencidos (8:00). Solo con CodigosBarras:InformeSemanal a true en Web.config.
            RecurringJob.AddOrUpdate(
                Infraestructure.Productos.InformeCodigosBarrasJobsService.ID_JOB,
                () => Infraestructure.Productos.InformeCodigosBarrasJobsService.Procesar(),
                Infraestructure.Productos.InformeCodigosBarrasJobsService.CRON, // Cron: los lunes a las 8:10
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'informe-codigos-barras' configurado (lunes a las 8:10; solo con el interruptor de Web.config)");

            // NestoAPI#670: pares «comprados juntos» y perfiles de las sugerencias de la plantilla de venta. De lunes a sábado a las
            // 05:00 (después de los jobs de la 1:00 a las 4:30; el domingo de madrugada corre el SP de precios medios, que bloquea
            // LinPedidoVta). Lee 24 meses de albaranes con NOLOCK por un solo índice (~3 s) y calcula en memoria. Cola «indicadores»
            // (servidor de un hilo de los jobs largos). Interruptor: ParámetrosUsuario «SugerenciasProductosJob» = Activo.
            RecurringJob.AddOrUpdate(
                Infraestructure.SugerenciasProductos.SugerenciasProductosJobsService.ID_JOB,
                () => Infraestructure.SugerenciasProductos.SugerenciasProductosJobsService.CalcularProgramado(),
                Infraestructure.SugerenciasProductos.SugerenciasProductosJobsService.CRON, // Cron: de lunes a sábado a las 05:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'sugerencias-productos-pares' configurado (de lunes a sábado a las 05:00)");

            // NestoAPI#673 (Carlos, 19/10/26): una reposición que lleva más de 2 días recibida en parte (desde la primera parcial),
            // aviso en la campana de Nesto a quienes hicieron las parciales y, si la recibe una tienda (REI, ALC), también a sus
            // usuarios (AlmacénPedidoVta, sin «(defecto)»); en Algete solo a quienes las hicieron. Una vez al día mientras siga a
            // medias. Ligero: una consulta de pendientes por almacén. Nunca rompe (ELMAH).
            RecurringJob.AddOrUpdate(
                Infraestructure.PreparacionAlmacen.AvisoReposicionesRecibidasEnParteJobsService.ID_JOB,
                () => Infraestructure.PreparacionAlmacen.AvisoReposicionesRecibidasEnParteJobsService.Avisar(),
                Infraestructure.PreparacionAlmacen.AvisoReposicionesRecibidasEnParteJobsService.CRON, // Cron: de lunes a sábado a las 9:15
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'reposiciones-recibidas-en-parte' configurado (de lunes a sábado a las 9:15)");

            // NestoAPI#693: pedidos con la fecha de entrega a la agencia prometida en riesgo. Lo de cada movimiento que pasa por
            // la API va por eventos (EventosPromesaRiesgo, un job un minuto después); esta pasada nocturna recalcula, fuera de
            // horario, todos los pedidos pendientes con promesa de hoy o futura (unas decenas) para lo que no pasa por la API
            // (Nesto viejo: regularizaciones de inventario, albaranes de compra, cambios del calendario...). Y el correo resumen,
            // L-V a las 9:00 y a las 15:00. Interruptor Pedidos:AvisoPromesaRiesgo (Apagado/Sombra/Activo) en Web.config.
            // NestoAPI#715: etiquetado nocturno de PedidosAnomalias (CorregidoEn48h / Abonado / SinCambios) con LinPedidoVta y la
            // tabla Modificaciones. A las 5:30, fuera de horario (recorre Modificaciones, sin índice por fecha). Interruptor
            // Anomalias:Modo (Apagado/Sombra/Activo) en Web.config; Apagado no hace nada.
            RecurringJob.AddOrUpdate(
                Infraestructure.PedidosVenta.Anomalias.AnomaliasJobsService.ID_JOB_ETIQUETADO,
                () => Infraestructure.PedidosVenta.Anomalias.AnomaliasJobsService.EtiquetarNocturno(),
                Infraestructure.PedidosVenta.Anomalias.AnomaliasJobsService.CRON_ETIQUETADO, // Cron: todos los días a las 5:30
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'anomalias-etiquetado' configurado (diario a las 5:30)");

            // NestoAPI#698: pedidos retenidos «hasta nuevo aviso» (modo de servicio 5). De lunes a sábado a las 5:35, fuera de horario y
            // antes de la pasada de promesa en riesgo: libera solos los que llegan a su fecha de revisión (vuelven a su modo y entran
            // en el picking) avisando por la campana a quien lo retuvo y al vendedor, y recuerda los que no tienen fecha como mucho
            // cada Pedidos:Retenidos:DiasRecordatorio días (7). Sin la tabla (Scripts/Issue698_PedidosRetenidos.sql) no hace nada.
            RecurringJob.AddOrUpdate(
                Infraestructure.PedidosVenta.Retencion.PedidosRetenidosJobsService.ID_JOB,
                () => Infraestructure.PedidosVenta.Retencion.PedidosRetenidosJobsService.PasadaNocturna(),
                Infraestructure.PedidosVenta.Retencion.PedidosRetenidosJobsService.CRON, // Cron: de lunes a sábado a las 5:35
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Job recurrente 'pedidos-retenidos-nocturna' configurado (L-S a las 5:35)");

            RecurringJob.AddOrUpdate(
                Infraestructure.PedidosVenta.PromesaRiesgoJobsService.ID_JOB_NOCTURNO,
                () => Infraestructure.PedidosVenta.PromesaRiesgoJobsService.PasadaNocturna(),
                Infraestructure.PedidosVenta.PromesaRiesgoJobsService.CRON_NOCTURNO, // Cron: de lunes a sábado a las 5:40
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            RecurringJob.AddOrUpdate(
                Infraestructure.PedidosVenta.PromesaRiesgoJobsService.ID_JOB_CORREO,
                () => Infraestructure.PedidosVenta.PromesaRiesgoJobsService.EnviarCorreoResumen(),
                Infraestructure.PedidosVenta.PromesaRiesgoJobsService.CRON_CORREO, // Cron: de lunes a viernes a las 9:00 y a las 15:00
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Jobs recurrentes 'promesa-riesgo-nocturna' (L-S 5:40) y 'promesa-riesgo-correo' (L-V 9:00 y 15:00) configurados");

            // NestoAPI#714 (corte 1, épica #689): clientes que se están yendo (abandono), EN SOMBRA. De lunes a sábado de madrugada
            // (fuera de horario): a las 5:10 se etiquetan las filas de ClientesRiesgoAbandono cuyo horizonte ha vencido
            // (Compró/Abandono/Baja/Desconocido) y a las 5:20 se evalúa la regla baseline (días sin comprar > 1,5 × cadencia) sobre
            // la cartera de llamadas de cada vendedor activo con la instantánea del día anterior; los lunes, además, la instantánea
            // de todos los compradores regulares con sus features (datos para el modelo de ML.NET del corte 2). Ligeros (segundos),
            // en la cola «default». Nadie ve nada: solo GET api/Clientes/RiesgoAbandono/Informe. Interruptor Abandono:Modo
            // (Apagado/Sombra/Activo) en Web.config; sin la tabla (Scripts/Issue714_ClientesRiesgoAbandono.sql) no hacen nada.
            RecurringJob.AddOrUpdate(
                Infraestructure.Abandono.RiesgoAbandonoJobsService.ID_JOB_RESOLVER,
                () => Infraestructure.Abandono.RiesgoAbandonoJobsService.ResolverVencidas(),
                Infraestructure.Abandono.RiesgoAbandonoJobsService.CRON_RESOLVER, // Cron: de lunes a sábado a las 5:10
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            // NestoAPI#716 (2.1, interino): mientras los vendedores no marquen el resultado del rapport, se clasifican con OpenAI
            // los Comentarios de los rapports de ayer y de hoy al mismo catálogo (SeguimientoClienteClasificacion). Todos los
            // días a las 23:15 (fuera de horario; el job sale si no es de noche). Con Rapports:ClasificacionBackfill = true
            // (por defecto apagado) rellena además 2024-2026 por tandas de 3.000 por noche (~3 USD en total con gpt-4o-mini).
            // Sin la tabla (Scripts/Issue716 sin lanzar) o sin OpenAIKey no hace nada.
            RecurringJob.AddOrUpdate(
                Infraestructure.Rapports.ClasificacionRapportsJobsService.ID_JOB,
                () => Infraestructure.Rapports.ClasificacionRapportsJobsService.Procesar(),
                Infraestructure.Rapports.ClasificacionRapportsJobsService.CRON, // Cron: todos los días a las 23:15
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            RecurringJob.AddOrUpdate(
                Infraestructure.Abandono.RiesgoAbandonoJobsService.ID_JOB_DIARIO,
                () => Infraestructure.Abandono.RiesgoAbandonoJobsService.PasadaDiaria(),
                Infraestructure.Abandono.RiesgoAbandonoJobsService.CRON_DIARIO, // Cron: de lunes a sábado a las 5:20
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                }
            );
            Console.WriteLine("✅ Jobs recurrentes 'riesgo-abandono-resolver' (L-S 5:10) y 'riesgo-abandono-diario' (L-S 5:20) configurados (sombra)");
            Console.WriteLine("✅ Job recurrente 'clasificar-rapports-nocturno' configurado (diario a las 23:15; solo con la tabla y OpenAIKey)");
        }
    }
}
