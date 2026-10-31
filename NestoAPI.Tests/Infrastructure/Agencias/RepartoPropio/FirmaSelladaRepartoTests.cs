using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#770: la firma de la entrega, sellada por el servidor (texto de conformidad, quién, cuándo y dónde) y guardada en
    /// el contenedor «firmas»; si el sellado o el blob fallan, la entrega no falla y la firma queda en la columna.
    /// </summary>
    [TestClass]
    public class FirmaSelladaRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 31, 12, 30, 5);
        private const string USUARIO = @"NUEVAVISION\Repartidor";

        private ServicioRepartoPropioTests.RepositorioFalso repositorio;
        private AlmacenFirmasFalso almacen;
        private GeocodificadorFalso geocodificador;
        private List<Exception> errores;
        private ServicioRepartoPropio servicio;

        [TestInitialize]
        public void Setup()
        {
            repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            repositorio.Con(ServicioRepartoPropioTests.Envio());
            almacen = new AlmacenFirmasFalso();
            geocodificador = new GeocodificadorFalso { Respuesta = "Calle Mayor 1, 28110 Algete" };
            errores = new List<Exception>();
            servicio = Servicio();
        }

        private ServicioRepartoPropio Servicio(DependenciasFirmasReparto firmas = null)
            => new ServicioRepartoPropio(repositorio, () => AHORA, firmas: firmas ?? new DependenciasFirmasReparto
            {
                Almacen = almacen,
                Geocodificador = geocodificador,
                RegistrarError = errores.Add,
                TiempoMaximoDireccion = TimeSpan.FromMilliseconds(300)
            });

        internal static byte[] FirmaPng(int ancho = 300, int alto = 150)
        {
            using (var bitmap = new Bitmap(ancho, alto, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                using (var lapiz = new Pen(Color.DimGray, 3))
                {
                    g.Clear(Color.Transparent);
                    g.DrawBezier(lapiz, 10, alto - 20, 80, 10, 160, alto, ancho - 10, 20);
                }
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        private static Size Medidas(byte[] png)
        {
            using (var ms = new MemoryStream(png))
            using (Image imagen = Image.FromStream(ms))
            {
                return imagen.Size;
            }
        }

        private static bool EsPng(byte[] bytes)
            => bytes != null && bytes.Length > 8 && bytes.Take(8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        private EntregaRepartoDTO Entrega(byte[] firma = null, decimal? latitud = 40.5967m, decimal? longitud = -3.4988m) => new EntregaRepartoDTO
        {
            ClaveIdempotencia = Guid.NewGuid(),
            FechaHora = AHORA,
            Receptor = "María López",
            DniReceptor = "12345678Z",
            FirmaPng = Convert.ToBase64String(firma ?? FirmaPng()),
            Latitud = latitud,
            Longitud = longitud,
            TextoConformidad = "Confirmo que recibo 3 bultos del pedido 928000 de Nueva Visión en buen estado aparente."
        };

        // ================================================================ texto de conformidad (la regla, igual que en Ariadna)

        [TestMethod]
        public void Texto_Entrega_SingularPluralYPedido()
        {
            Assert.AreEqual("Confirmo que recibo 1 bulto del pedido 928000 de Nueva Visión en buen estado aparente.",
                TextoConformidadEntrega.Entrega(1, 928000, FormaPagoConformidad.Ninguno, null));
            Assert.AreEqual("Confirmo que recibo 3 bultos del pedido 928000 de Nueva Visión en buen estado aparente.",
                TextoConformidadEntrega.Entrega(3, 928000, FormaPagoConformidad.Ninguno, 45.3m));
            Assert.AreEqual("Confirmo que recibo 2 bultos de Nueva Visión en buen estado aparente.",
                TextoConformidadEntrega.Entrega(2, null, FormaPagoConformidad.Ninguno, null));
        }

        [TestMethod]
        public void Texto_Entrega_ConPagoEnEfectivoOTarjeta()
        {
            Assert.AreEqual("Confirmo que recibo 3 bultos del pedido 928000 de Nueva Visión en buen estado aparente y que he pagado 45,30 € en efectivo al repartidor.",
                TextoConformidadEntrega.Entrega(3, 928000, FormaPagoConformidad.Efectivo, 45.3m));
            Assert.AreEqual("Confirmo que recibo 1 bulto del pedido 928000 de Nueva Visión en buen estado aparente y que he pagado 45,30 € con tarjeta.",
                TextoConformidadEntrega.Entrega(1, 928000, FormaPagoConformidad.Tarjeta, 45.30m));
            // Sin importe no se dice que se ha pagado
            Assert.AreEqual("Confirmo que recibo 1 bulto del pedido 928000 de Nueva Visión en buen estado aparente.",
                TextoConformidadEntrega.Entrega(1, 928000, FormaPagoConformidad.Efectivo, 0m));
        }

        [TestMethod]
        public void Texto_Recogida()
        {
            Assert.AreEqual("Confirmo que entrego 1 bulto al repartidor de Nueva Visión.", TextoConformidadEntrega.Recogida(1));
            Assert.AreEqual("Confirmo que entrego 4 bultos al repartidor de Nueva Visión.", TextoConformidadEntrega.Recogida(4));
        }

        [TestMethod]
        public void Texto_FormaDePago_DelCobro()
        {
            Assert.AreEqual(FormaPagoConformidad.Efectivo, TextoConformidadEntrega.FormaDe("Efectivo"));
            Assert.AreEqual(FormaPagoConformidad.Tarjeta, TextoConformidadEntrega.FormaDe("Tarjeta"));
            Assert.AreEqual(FormaPagoConformidad.Ninguno, TextoConformidadEntrega.FormaDe("Enlace"));
            Assert.AreEqual(FormaPagoConformidad.Ninguno, TextoConformidadEntrega.FormaDe("NoCobrado"));
            Assert.AreEqual(FormaPagoConformidad.Ninguno, TextoConformidadEntrega.FormaDe(null));
        }

        [TestMethod]
        public void Texto_ElDeLaPdaMandaYSinElLaMismaRegla()
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(reembolso: 45.3m);
            Assert.AreEqual("Lo que vio", ServicioRepartoPropio.TextoConformidad(new EntregaRepartoDTO { TextoConformidad = "  Lo que vio " }, envio, false, null, "Efectivo", 45.3m));
            Assert.AreEqual(EntregaRepartoDTO.MAXIMO_TEXTO_CONFORMIDAD,
                ServicioRepartoPropio.TextoConformidad(new EntregaRepartoDTO { TextoConformidad = new string('x', 900) }, envio, false, null, null, null).Length);

            Assert.AreEqual("Confirmo que recibo 3 bultos del pedido 928000 de Nueva Visión en buen estado aparente y que he pagado 45,30 € en efectivo al repartidor.",
                ServicioRepartoPropio.TextoConformidad(new EntregaRepartoDTO(), envio, false, null, "Efectivo", 45.3m));
            Assert.AreEqual("Confirmo que recibo 3 bultos del pedido 928000 de Nueva Visión en buen estado aparente.",
                ServicioRepartoPropio.TextoConformidad(new EntregaRepartoDTO(), envio, false, null, "Enlace", 45.3m));
            Assert.AreEqual("Confirmo que entrego 2 bultos al repartidor de Nueva Visión.",
                ServicioRepartoPropio.TextoConformidad(new EntregaRepartoDTO(), envio, true, 2, null, null));
            Assert.AreEqual("Confirmo que entrego 3 bultos al repartidor de Nueva Visión.",
                ServicioRepartoPropio.TextoConformidad(null, envio, true, null, null, null), "Sin bultos recogidos, los de la nota");
        }

        // ================================================================ el sello

        [TestMethod]
        public void Sello_PngDeAnchoFijoConLaFirmaYElTextoDebajo()
        {
            byte[] sellada = SelloFirmaEntrega.Sellar(FirmaPng(300, 150), new DatosSelloFirma
            {
                TextoConformidad = "Confirmo que recibo 3 bultos del pedido 928000 de Nueva Visión en buen estado aparente y que he pagado 45,30 € en efectivo al repartidor.",
                FechaHora = AHORA,
                Receptor = "María López",
                DniReceptor = "12345678Z",
                Latitud = 40.5967m,
                Longitud = -3.4988m,
                Direccion = "Calle Mayor 1, 28110 Algete"
            });

            Assert.IsTrue(EsPng(sellada));
            Size medidas = Medidas(sellada);
            Assert.AreEqual(SelloFirmaEntrega.ANCHO, medidas.Width);
            // La firma (150 px ampliada hasta 260) + el texto debajo
            Assert.IsTrue(medidas.Height > 260 + 100 && medidas.Height < 700, $"Alto {medidas.Height}");
        }

        [TestMethod]
        public void Sello_LasLineasSonSoloLasDichas()
        {
            var datos = new DatosSelloFirma { FechaHora = AHORA, Receptor = " María ", DniReceptor = "12345678Z", Latitud = 40.5967m, Longitud = -3.4988m };
            CollectionAssert.AreEqual(new[] { "Nombre: María · DNI: 12345678Z", "Fecha: 31/10/2026 12:30:05", "Ubicación: 40.596700, -3.498800" },
                SelloFirmaEntrega.LineasDatos(datos));

            datos.Direccion = "Calle Mayor 1, 28110 Algete";
            Assert.AreEqual("Ubicación: Calle Mayor 1, 28110 Algete (40.596700, -3.498800)", SelloFirmaEntrega.TextoUbicacion(datos));

            var sinNada = new DatosSelloFirma { FechaHora = AHORA };
            CollectionAssert.AreEqual(new[] { "Fecha: 31/10/2026 12:30:05", "Sin ubicación" }, SelloFirmaEntrega.LineasDatos(sinNada));
        }

        [TestMethod]
        public void Sello_FirmaGrandeSeReduceYNoEsImagenLanza()
        {
            Size medidas = Medidas(SelloFirmaEntrega.Sellar(FirmaPng(1600, 900), new DatosSelloFirma { FechaHora = AHORA }));
            Assert.AreEqual(SelloFirmaEntrega.ANCHO, medidas.Width);
            Assert.IsTrue(medidas.Height < 420, $"Alto {medidas.Height}");

            _ = Assert.ThrowsException<ArgumentException>(() => SelloFirmaEntrega.Sellar(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 }, new DatosSelloFirma()));
        }

        // ================================================================ entregar: sellada al blob

        [TestMethod]
        public async Task Entregar_SubeLaFirmaSelladaAlBlobYLaColumnaQuedaVacia()
        {
            ResultadoReparto<EstadoEnvioRepartoDTO> resultado = await servicio.Entregar(251300, Entrega(), USUARIO);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo);
            ServicioRepartoPropioTests.Registro registro = repositorio.Registros.Single();
            Assert.AreEqual("1/251300/" + registro.Prueba.Id + "-20261031123005.png", registro.RutaBlob);
            Assert.IsNull(registro.Firma, "Las nuevas solo en el blob");
            byte[] subida = almacen.Blobs[registro.RutaBlob];
            Assert.AreEqual(SelloFirmaEntrega.ANCHO, Medidas(subida).Width, "Se guarda la sellada, no la limpia");
            Assert.AreEqual(1, geocodificador.Llamadas);
            Assert.AreEqual(0, errores.Count);

            // La prueba dice que tiene firma y la firma se lee del blob
            Assert.IsTrue((await servicio.PruebaEntrega(251300)).Contenido.TieneFirma);
            CollectionAssert.AreEqual(subida, (await servicio.Firma(251300)).Contenido);
        }

        [TestMethod]
        public async Task Entregar_SiFallaLaGeocodificacionSellaConCoordenadas()
        {
            geocodificador.Fallo = new HttpRequestException("Nominatim caído");

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, Entrega(), USUARIO)).Tipo);

            ServicioRepartoPropioTests.Registro registro = repositorio.Registros.Single();
            Assert.IsNotNull(registro.RutaBlob);
            Assert.AreEqual(SelloFirmaEntrega.ANCHO, Medidas(almacen.Blobs[registro.RutaBlob]).Width);
        }

        [TestMethod]
        public async Task Entregar_SiLaGeocodificacionTardaNoEspera()
        {
            geocodificador.Espera = TimeSpan.FromSeconds(10);
            DateTime inicio = DateTime.UtcNow;

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, Entrega(), USUARIO)).Tipo);

            Assert.IsTrue(DateTime.UtcNow - inicio < TimeSpan.FromSeconds(5));
            Assert.IsNotNull(repositorio.Registros.Single().RutaBlob);
        }

        [TestMethod]
        public async Task Entregar_SinGpsNoGeocodifica()
        {
            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, Entrega(latitud: null, longitud: null), USUARIO)).Tipo);
            Assert.AreEqual(0, geocodificador.Llamadas);
            Assert.IsNotNull(repositorio.Registros.Single().RutaBlob);
        }

        [TestMethod]
        public async Task Entregar_SiFallaElBlobLaFirmaSelladaQuedaEnLaColumna()
        {
            almacen.FalloSubir = new InvalidOperationException("403");

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, Entrega(), USUARIO)).Tipo);

            ServicioRepartoPropioTests.Registro registro = repositorio.Registros.Single();
            Assert.IsNull(registro.RutaBlob);
            Assert.AreEqual(SelloFirmaEntrega.ANCHO, Medidas(registro.Firma).Width);
            Assert.AreEqual(1, errores.Count);
            Assert.IsFalse(errores[0].Message.Contains("12345678Z"), "Nunca el DNI en ELMAH");
            CollectionAssert.AreEqual(registro.Firma, (await servicio.Firma(251300)).Contenido);
        }

        [TestMethod]
        public async Task Entregar_SiFallaGuardarLaRutaLaFirmaSigueEnLaColumna()
        {
            repositorio.FalloGuardarRutaBlob = new InvalidOperationException("timeout");

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, Entrega(), USUARIO)).Tipo);

            Assert.IsNotNull(repositorio.Registros.Single().Firma);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task Entregar_SiNoSePuedeSellarGuardaLaLimpiaEnLaColumnaSinSubir()
        {
            byte[] noDecodificable = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, Entrega(noDecodificable), USUARIO)).Tipo);

            ServicioRepartoPropioTests.Registro registro = repositorio.Registros.Single();
            CollectionAssert.AreEqual(noDecodificable, registro.Firma);
            Assert.IsNull(registro.RutaBlob);
            Assert.AreEqual(0, almacen.Blobs.Count, "La migración la subirá después");
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task Entregar_SinColumnaRutaBlobOSinAlmacenSellaEnLaColumna()
        {
            repositorio.ColumnaRutaBlob = false;
            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, Entrega(), USUARIO)).Tipo);
            Assert.AreEqual(SelloFirmaEntrega.ANCHO, Medidas(repositorio.Registros.Single().Firma).Width);
            Assert.AreEqual(0, almacen.Blobs.Count);

            repositorio.Registros.Clear();
            repositorio.ColumnaRutaBlob = true;
            repositorio.Con(ServicioRepartoPropioTests.Envio());
            ServicioRepartoPropio sinAlmacen = Servicio(new DependenciasFirmasReparto { RegistrarError = errores.Add });
            Assert.AreEqual(TipoResultadoReparto.Ok, (await sinAlmacen.Entregar(251300, Entrega(), USUARIO)).Tipo);
            Assert.IsNotNull(repositorio.Registros.Single().Firma);
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public async Task Entregar_ReintentoConLaMismaClaveNoVuelveASubir()
        {
            EntregaRepartoDTO peticion = Entrega();
            _ = await servicio.Entregar(251300, peticion, USUARIO);
            Assert.AreEqual(1, almacen.Subidas);

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(251300, peticion, USUARIO)).Tipo);
            Assert.AreEqual(1, almacen.Subidas);
        }

        // ================================================================ leer la firma

        [TestMethod]
        public async Task Firma_EnElBlobQueNoSePuedeLeer_Invalido()
        {
            _ = await servicio.Entregar(251300, Entrega(), USUARIO);
            almacen.FalloDescargar = new InvalidOperationException("404");

            ResultadoReparto<byte[]> firma = await servicio.Firma(251300);

            Assert.AreEqual(TipoResultadoReparto.Invalido, firma.Tipo);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task Firma_ViejaEnLaColumnaSeSigueLeyendo()
        {
            byte[] vieja = FirmaPng();
            _ = repositorio.ConFirmaVieja(251300, vieja, AHORA.AddDays(-1));

            CollectionAssert.AreEqual(vieja, (await servicio.Firma(251300)).Contenido);
            Assert.AreEqual(0, almacen.Descargas);
        }

        // ================================================================ migración

        [TestMethod]
        public async Task Migrar_SinAplicarSoloCuentaYLista()
        {
            _ = repositorio.ConFirmaVieja(251300, FirmaPng(), new DateTime(2026, 10, 29, 10, 0, 0));
            _ = repositorio.ConFirmaVieja(251301, FirmaPng(), new DateTime(2026, 10, 30, 11, 15, 0));

            ResultadoReparto<ResultadoMigracionFirmasDTO> resultado = await servicio.MigrarFirmas(false, 100);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo);
            Assert.IsFalse(resultado.Contenido.Aplicado);
            Assert.AreEqual(2, resultado.Contenido.Pendientes);
            Assert.AreEqual(0, resultado.Contenido.Migradas);
            Assert.AreEqual(2, resultado.Contenido.Firmas.Count);
            Assert.AreEqual("1/251300/1-20261029100000.png", resultado.Contenido.Firmas[0].RutaBlob);
            Assert.IsTrue(resultado.Contenido.Firmas[0].Bytes > 0);
            Assert.AreEqual(0, almacen.Subidas, "aplicar=false no escribe");
            Assert.IsTrue(repositorio.Registros.All(r => r.Firma != null && r.RutaBlob == null));
        }

        [TestMethod]
        public async Task Migrar_AplicandoSubeEnLotesYEsIdempotente()
        {
            byte[] vieja = FirmaPng();
            _ = repositorio.ConFirmaVieja(251300, vieja, new DateTime(2026, 10, 29, 10, 0, 0));
            _ = repositorio.ConFirmaVieja(251301, FirmaPng(), new DateTime(2026, 10, 30, 11, 15, 0));
            _ = repositorio.ConFirmaVieja(251302, FirmaPng(), new DateTime(2026, 10, 30, 12, 0, 0));

            ResultadoMigracionFirmasDTO lote1 = (await servicio.MigrarFirmas(true, 2)).Contenido;
            Assert.AreEqual(3, lote1.Pendientes);
            Assert.AreEqual(2, lote1.Migradas);
            CollectionAssert.AreEqual(vieja, almacen.Blobs["1/251300/1-20261029100000.png"], "Las viejas, tal cual");
            Assert.AreEqual("1/251300/1-20261029100000.png", repositorio.Registros[0].RutaBlob);
            Assert.IsNull(repositorio.Registros[0].Firma);

            ResultadoMigracionFirmasDTO lote2 = (await servicio.MigrarFirmas(true, 2)).Contenido;
            Assert.AreEqual(1, lote2.Pendientes);
            Assert.AreEqual(1, lote2.Migradas);

            ResultadoMigracionFirmasDTO lote3 = (await servicio.MigrarFirmas(true, 2)).Contenido;
            Assert.AreEqual(0, lote3.Pendientes);
            Assert.AreEqual(0, lote3.Firmas.Count);
            Assert.AreEqual(3, almacen.Subidas);
        }

        [TestMethod]
        public async Task Migrar_UnFalloNoParaElLote()
        {
            _ = repositorio.ConFirmaVieja(251300, FirmaPng(), AHORA);
            _ = repositorio.ConFirmaVieja(251301, FirmaPng(), AHORA);
            almacen.FallarEnRuta = "1/251300/1-20261031123005.png";

            ResultadoMigracionFirmasDTO resultado = (await servicio.MigrarFirmas(true, 100)).Contenido;

            Assert.AreEqual(1, resultado.Migradas);
            Assert.AreEqual(1, resultado.Errores);
            Assert.IsNotNull(resultado.Firmas[0].Error);
            Assert.IsNotNull(repositorio.Registros[0].Firma, "La que falla sigue en la columna");
        }

        [TestMethod]
        public async Task Migrar_SinScriptOSinAlmacen_Invalido()
        {
            repositorio.ColumnaRutaBlob = false;
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.MigrarFirmas(false, 100)).Tipo);

            repositorio.ColumnaRutaBlob = true;
            ServicioRepartoPropio sinAlmacen = Servicio(new DependenciasFirmasReparto());
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await sinAlmacen.MigrarFirmas(true, 100)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Ok, (await sinAlmacen.MigrarFirmas(false, 100)).Tipo, "Contar no necesita el almacenamiento");
        }

        // ================================================================ controlador

        private static ClaimsPrincipal Usuario(params string[] grupos)
            => new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"NUEVAVISION\alguien") }
                .Concat(grupos.Select(g => new Claim(ClaimTypes.Role, @"NUEVAVISION\" + g))), "Bearer"));

        private RepartoPropioController Controlador(ClaimsPrincipal usuario)
            => new RepartoPropioController(A.Fake<NVEntities>(), servicio)
            {
                User = usuario,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };

        [TestMethod]
        public async Task Controlador_MigrarSoloDireccionEInformatica()
        {
            _ = repositorio.ConFirmaVieja(251300, FirmaPng(), AHORA);

            foreach (string grupo in new[] { "Almacén", "Administración", "Tiendas" })
            {
                HttpResponseMessage prohibido = await (await Controlador(Usuario(grupo)).MigrarFirmas(true, 100)).ExecuteAsync(CancellationToken.None);
                Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode, grupo);
            }
            Assert.AreEqual(0, almacen.Subidas);

            HttpResponseMessage informatica = await (await Controlador(Usuario("Informática")).MigrarFirmas()).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.OK, informatica.StatusCode);
            Assert.AreEqual(0, almacen.Subidas, "Por defecto aplicar=false");

            HttpResponseMessage direccion = await (await Controlador(Usuario("Dirección")).MigrarFirmas(true, 100)).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.OK, direccion.StatusCode);
            Assert.AreEqual(1, almacen.Subidas);
        }

        [TestMethod]
        public async Task Controlador_FirmaDelBlobComoPngSinCache()
        {
            _ = await servicio.Entregar(251300, Entrega(), USUARIO);

            HttpResponseMessage respuesta = await (await Controlador(Usuario("Administración")).GetFirma(251300)).ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            Assert.AreEqual("image/png", respuesta.Content.Headers.ContentType.MediaType);
            Assert.IsTrue(respuesta.Headers.CacheControl.NoStore);
            byte[] png = await respuesta.Content.ReadAsByteArrayAsync();
            Assert.AreEqual(SelloFirmaEntrega.ANCHO, Medidas(png).Width);

            HttpResponseMessage informatica = await (await Controlador(Usuario("Informática")).GetFirma(251300)).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.Forbidden, informatica.StatusCode, "Informática migra pero no ve firmas");
        }

        // ================================================================ geocodificación inversa (lectura del JSON)

        [TestMethod]
        public void GeocodificacionInversa_LeeCalleNumeroCpYPoblacion()
        {
            Assert.AreEqual("Calle Mayor 1, 28110 Algete", GeocodificadorInversoNominatim.Leer(
                "{\"display_name\":\"1, Calle Mayor, Algete, Comunidad de Madrid, 28110, España\",\"address\":{\"house_number\":\"1\",\"road\":\"Calle Mayor\",\"town\":\"Algete\",\"postcode\":\"28110\"}}"));
            Assert.AreEqual("Calle Mayor, Algete", GeocodificadorInversoNominatim.Leer("{\"address\":{\"road\":\"Calle Mayor\",\"city\":\"Algete\"}}"));
            Assert.AreEqual("Algo, España", GeocodificadorInversoNominatim.Leer("{\"display_name\":\"Algo, España\",\"address\":{\"country\":\"España\"}}"));
            Assert.IsNull(GeocodificadorInversoNominatim.Leer("{\"error\":\"Unable to geocode\"}"));
            Assert.IsNull(GeocodificadorInversoNominatim.Leer(""));
            StringAssert.Contains(GeocodificadorInversoNominatim.Url(40.5967m, -3.4988m), "lat=40.5967&lon=-3.4988");
            Assert.AreEqual(GeocodificadorInversoNominatim.ClaveCache(40.59671m, -3.49881m), GeocodificadorInversoNominatim.ClaveCache(40.59674m, -3.49884m));
        }

        // ================================================================ dobles

        internal class AlmacenFirmasFalso : IAlmacenFirmasEntrega
        {
            public readonly Dictionary<string, byte[]> Blobs = new Dictionary<string, byte[]>();
            public Exception FalloSubir, FalloDescargar;
            public string FallarEnRuta;
            public int Subidas, Descargas;

            public bool Configurado => true;

            public Task Subir(string ruta, byte[] png)
            {
                if (FalloSubir != null || ruta == FallarEnRuta)
                {
                    throw FalloSubir ?? new InvalidOperationException("fallo en " + ruta);
                }
                Subidas++;
                Blobs[ruta] = png;
                return Task.CompletedTask;
            }

            public Task<byte[]> Descargar(string ruta)
            {
                Descargas++;
                if (FalloDescargar != null)
                {
                    throw FalloDescargar;
                }
                return Task.FromResult(Blobs[ruta]);
            }
        }

        internal class GeocodificadorFalso : IGeocodificadorInverso
        {
            public string Respuesta;
            public Exception Fallo;
            public TimeSpan Espera = TimeSpan.Zero;
            public int Llamadas;

            public async Task<string> Direccion(decimal latitud, decimal longitud)
            {
                Llamadas++;
                if (Espera > TimeSpan.Zero)
                {
                    await Task.Delay(Espera);
                }
                if (Fallo != null)
                {
                    throw Fallo;
                }
                return Respuesta;
            }
        }
    }
}
