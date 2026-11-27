using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#868: el mozo puede mandar con el aviso de ficha una foto hecha por él del producto que tiene en la mano
    /// (opcional, JPEG en base64). Se guarda en el blob (avisos-ficha/…), su ruta en la fila del aviso y va dentro del
    /// correo como segundo recurso, junto a la de la tienda. Con varios informantes, la última foto.
    /// </summary>
    [TestClass]
    public class AvisosFichaFotoMozoTests
    {
        private const string EMPRESA = "1";
        private static readonly byte[] JPEG = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        private static readonly string JPEG_BASE64 = Convert.ToBase64String(JPEG);
        private static readonly Guid CLAVE = new Guid("0a0b0c0d-0000-4000-8000-000000000868");

        private RepositorioAvisosFichaFalso repositorio;
        private IAvisadorFichaProducto avisador;
        private IFotosProductoAlmacen fotos;
        private IAlmacenFotosBultos blob;
        private ServicioAvisosFicha servicio;
        private readonly List<AvisoFicha> avisados = new List<AvisoFicha>();

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioAvisosFichaFalso();
            repositorio.Fichas["43449"] = new DatosFichaActual { Producto = "43449", Nombre = "CREMA", CodigoBarras = "8436000000002", Precio = 10M };
            avisador = A.Fake<IAvisadorFichaProducto>();
            A.CallTo(() => avisador.AvisarEquipo(A<AvisoFicha>._, A<DatosFichaActual>._, A<IReadOnlyCollection<string>>._))
                .Invokes((AvisoFicha a, DatosFichaActual f, IReadOnlyCollection<string> c) => avisados.Add(new AvisoFicha { Producto = a.Producto, Destino = a.Destino, FotoMozo = a.FotoMozo, RutaFotoMozo = a.RutaFotoMozo }));
            fotos = A.Fake<IFotosProductoAlmacen>();
            A.CallTo(() => fotos.UrlActual("43449")).Returns("https://tienda.es/1-home_default/crema.jpg");
            blob = A.Fake<IAlmacenFotosBultos>();
            A.CallTo(() => blob.Configurado).Returns(true);
            servicio = new ServicioAvisosFicha(repositorio, avisador, fotos, blob, () => CLAVE);
        }

        private Task<ResultadoInformarDatoMal> Informar(string mozo, string foto, params string[] campos)
            => servicio.Informar(EMPRESA, new InformarDatoMalDTO
            {
                Producto = "43449",
                Campos = campos.Length == 0 ? new List<string> { "Foto" } : campos.ToList(),
                Comentario = "No es este envase",
                FotoMozoBase64 = foto
            }, mozo);

        // ---- La foto que llega (regla pura) ----

        [TestMethod]
        public void Leer_SinFoto_NoEsErrorYNoHayBytes()
        {
            Assert.IsNull(FotoMozoAvisoFicha.Leer(null, out byte[] bytes));
            Assert.IsNull(bytes);
            Assert.IsNull(FotoMozoAvisoFicha.Leer("   ", out bytes));
            Assert.IsNull(bytes);
        }

        [TestMethod]
        public void Leer_JpegEnBase64_ConOSinPrefijoData()
        {
            Assert.IsNull(FotoMozoAvisoFicha.Leer(JPEG_BASE64, out byte[] bytes));
            CollectionAssert.AreEqual(JPEG, bytes);
            Assert.IsNull(FotoMozoAvisoFicha.Leer("data:image/jpeg;base64," + JPEG_BASE64, out bytes));
            CollectionAssert.AreEqual(JPEG, bytes);
        }

        [TestMethod]
        public void Leer_NoEsBase64_Mensaje()
        {
            Assert.IsNotNull(FotoMozoAvisoFicha.Leer("esto no es base64 ¡!", out byte[] bytes));
            Assert.IsNull(bytes);
        }

        [TestMethod]
        public void Leer_NoEsJpeg_Mensaje()
        {
            string png = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A });
            StringAssert.Contains(FotoMozoAvisoFicha.Leer(png, out byte[] bytes), "JPEG");
            Assert.IsNull(bytes);
        }

        [TestMethod]
        public void Leer_PasaDelTope_Mensaje()
        {
            var grande = new byte[InformarDatoMalDTO.FOTO_MOZO_BYTES_MAXIMO + 1];
            grande[0] = 0xFF; grande[1] = 0xD8; grande[2] = 0xFF;
            StringAssert.Contains(FotoMozoAvisoFicha.Leer(Convert.ToBase64String(grande), out byte[] bytes), "demasiado grande");
            Assert.IsNull(bytes);
        }

        [TestMethod]
        public void Leer_JustoEnElTope_Vale()
        {
            var justa = new byte[InformarDatoMalDTO.FOTO_MOZO_BYTES_MAXIMO];
            justa[0] = 0xFF; justa[1] = 0xD8; justa[2] = 0xFF;
            Assert.IsNull(FotoMozoAvisoFicha.Leer(Convert.ToBase64String(justa), out byte[] bytes));
            Assert.AreEqual(justa.Length, bytes.Length);
        }

        [TestMethod]
        public void Dto_LaFotoEsOpcionalYElTopeEsDe4MB()
        {
            Assert.IsNull(new InformarDatoMalDTO().FotoMozoBase64);
            Assert.AreEqual(4 * 1024 * 1024, InformarDatoMalDTO.FOTO_MOZO_BYTES_MAXIMO);
            var leido = Newtonsoft.Json.JsonConvert.DeserializeObject<InformarDatoMalDTO>("{\"Producto\":\"43449\",\"Campos\":[\"Foto\"],\"FotoMozoBase64\":\"" + JPEG_BASE64 + "\"}");
            Assert.AreEqual(JPEG_BASE64, leido.FotoMozoBase64);
        }

        // ---- El servicio ----

        [TestMethod]
        public async Task ConFoto_SeGuardaEnElBlobYLaRutaVaEnElAvisoYEnElCorreo()
        {
            ResultadoInformarDatoMal resultado = await Informar("Andre", JPEG_BASE64);

            Assert.AreEqual(EstadoInformarDatoMal.Guardado, resultado.Estado);
            string ruta = "avisos-ficha/43449-" + CLAVE.ToString("N") + ".jpg";
            A.CallTo(() => blob.Subir(ruta, A<byte[]>.That.IsSameSequenceAs(JPEG), "image/jpeg")).MustHaveHappenedOnceExactly();
            AvisoFicha aviso = repositorio.Avisos.Single();
            Assert.AreEqual(ruta, aviso.RutaFotoMozo);
            CollectionAssert.AreEqual(JPEG, avisados.Single().FotoMozo);
        }

        [TestMethod]
        public async Task SinFoto_ComoHoy_NiBlobNiFotoEnElCorreo()
        {
            await Informar("Andre", null);

            A.CallTo(() => blob.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();
            Assert.IsNull(repositorio.Avisos.Single().RutaFotoMozo);
            Assert.IsNull(avisados.Single().FotoMozo);
        }

        [TestMethod]
        public async Task FotoNoValida_NoSeAvisaANadie()
        {
            ResultadoInformarDatoMal resultado = await Informar("Andre", Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }));

            Assert.AreEqual(EstadoInformarDatoMal.NoValido, resultado.Estado);
            Assert.AreEqual(0, repositorio.Avisos.Count);
            A.CallTo(() => blob.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task BlobSinConfigurar_ElAvisoSaleConLaFotoEnElCorreoPeroSinRuta()
        {
            A.CallTo(() => blob.Configurado).Returns(false);

            await Informar("Andre", JPEG_BASE64);

            A.CallTo(() => blob.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();
            Assert.IsNull(repositorio.Avisos.Single().RutaFotoMozo);
            CollectionAssert.AreEqual(JPEG, avisados.Single().FotoMozo);
        }

        [TestMethod]
        public async Task SinBlob_ElAvisoSaleConLaFotoEnElCorreo()
        {
            servicio = new ServicioAvisosFicha(repositorio, avisador, fotos);

            await Informar("Andre", JPEG_BASE64);

            Assert.IsNull(repositorio.Avisos.Single().RutaFotoMozo);
            CollectionAssert.AreEqual(JPEG, avisados.Single().FotoMozo);
        }

        [TestMethod]
        public async Task ElBlobFalla_ElAvisoSaleIgualConLaFotoEnElCorreo()
        {
            A.CallTo(() => blob.Subir(A<string>._, A<byte[]>._, A<string>._)).ThrowsAsync(new InvalidOperationException("403"));

            ResultadoInformarDatoMal resultado = await Informar("Andre", JPEG_BASE64);

            Assert.AreEqual(EstadoInformarDatoMal.Guardado, resultado.Estado);
            Assert.IsNull(repositorio.Avisos.Single().RutaFotoMozo);
            CollectionAssert.AreEqual(JPEG, avisados.Single().FotoMozo);
        }

        [TestMethod]
        public async Task DosEquipos_UnaSolaSubidaYLaFotoEnLosDosCorreos()
        {
            await Informar("Andre", JPEG_BASE64, "Foto", "Precio");

            A.CallTo(() => blob.Subir(A<string>._, A<byte[]>._, A<string>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(2, repositorio.Avisos.Count);
            Assert.IsTrue(repositorio.Avisos.All(a => a.RutaFotoMozo != null));
            Assert.AreEqual(2, avisados.Count(a => a.FotoMozo != null));
        }

        [TestMethod]
        public async Task VariosInformantes_MandaLaUltimaFoto_YSinFotoSeQuedaLaQueHabia()
        {
            var claves = new Queue<Guid>(new[] { new Guid("00000000-0000-4000-8000-000000000001"), new Guid("00000000-0000-4000-8000-000000000002") });
            servicio = new ServicioAvisosFicha(repositorio, avisador, fotos, blob, () => claves.Dequeue());

            await Informar("Andre", JPEG_BASE64, "Precio");
            await Informar("Pedro", JPEG_BASE64, "Precio", "Nombre");
            AvisoFicha aviso = repositorio.Avisos.Single();
            Assert.AreEqual("avisos-ficha/43449-00000000000040008000000000000002.jpg", aviso.RutaFotoMozo, "La última foto");
            CollectionAssert.AreEqual(JPEG, avisados.Last().FotoMozo);

            await Informar("Santi", null, "Precio", "Nombre", "Familia");
            Assert.AreEqual("avisos-ficha/43449-00000000000040008000000000000002.jpg", aviso.RutaFotoMozo, "Sin foto nueva, la que había");
            Assert.IsNull(avisados.Last().FotoMozo, "El correo de Santi no lleva foto del mozo");
        }

        [TestMethod]
        public async Task Comprobar_NoSeSubeNada()
        {
            ResultadoInformarDatoMal resultado = await servicio.Informar(EMPRESA, new InformarDatoMalDTO
            {
                Producto = "43449",
                Campos = new List<string> { "CodigoBarras" },
                CodigoLeido = "8436000000002",
                CodigoLeidoConEscaner = true,
                FotoMozoBase64 = JPEG_BASE64
            }, "Andre");

            Assert.AreEqual(EstadoInformarDatoMal.Comprobar, resultado.Estado);
            A.CallTo(() => blob.Subir(A<string>._, A<byte[]>._, A<string>._)).MustNotHaveHappened();
        }

        // ---- El SQL con la columna opcional ----

        [TestMethod]
        public void Sql_ConColumna_InsertaLaRutaYElUpdateNoBorraLaQueHabia()
        {
            StringAssert.Contains(RepositorioAvisosFichaSql.SQL_INSERTAR_CON_FOTO_MOZO, "Dispositivo, Veces, FotoMozo)");
            StringAssert.Contains(RepositorioAvisosFichaSql.SQL_INSERTAR_CON_FOTO_MOZO, "@dispositivo, @veces, @fotoMozo)");
            StringAssert.Contains(RepositorioAvisosFichaSql.SQL_ACTUALIZAR_CON_FOTO_MOZO, "FotoMozo = COALESCE(@fotoMozo, FotoMozo)");
            Assert.IsFalse(RepositorioAvisosFichaSql.SQL_INSERTAR.Contains("FotoMozo"), "Sin la columna, el SQL de siempre");
            Assert.IsFalse(RepositorioAvisosFichaSql.SQL_ACTUALIZAR.Contains("FotoMozo"));
        }

        // ---- El correo ----

        private static AvisoFicha AvisoCorreo(byte[] fotoMozo, string urlFoto = "https://tienda.es/1-home_default/crema.jpg") => new AvisoFicha
        {
            Id = 9, Clave = new Guid("6f9c1d2e-0000-4000-8000-000000000868"), Empresa = "1", Producto = "43449", Destino = DestinosAvisoFicha.TIENDA_ONLINE,
            Campos = new List<string> { "Foto" }, Informantes = new List<string> { "Andre" }, UrlFoto = urlFoto, FotoMozo = fotoMozo
        };

        private static string Html(MailMessage mail)
        {
            using (var lector = new System.IO.StreamReader(mail.AlternateViews.Last().ContentStream))
            {
                return lector.ReadToEnd();
            }
        }

        [TestMethod]
        public void Correo_LasDosFotos_DosRecursosConSusTitulos()
        {
            var tienda = new FotoCorreo { Bytes = new byte[] { 0xFF, 0xD8, 0xFF, 9 }, TipoMime = "image/jpeg" };

            MailMessage mail = AvisadorFichaProducto.Correo(AvisoCorreo(JPEG), new DatosFichaActual { Producto = "43449" }, new[] { "Foto" }, "t", null, null, tienda);

            Assert.AreEqual(2, mail.AlternateViews.Count);
            LinkedResourceCollection recursos = mail.AlternateViews.Last().LinkedResources;
            CollectionAssert.AreEqual(new[] { AvisadorFichaProducto.ID_FOTO, AvisadorFichaProducto.ID_FOTO_MOZO }, recursos.Select(r => r.ContentId).ToArray());
            LinkedResource mozo = recursos.Single(r => r.ContentId == AvisadorFichaProducto.ID_FOTO_MOZO);
            Assert.AreEqual("image/jpeg", mozo.ContentType.MediaType);
            Assert.AreEqual("mozo-43449.jpg", mozo.ContentType.Name);
            var leidos = new System.IO.MemoryStream();
            mozo.ContentStream.CopyTo(leidos);
            CollectionAssert.AreEqual(JPEG, leidos.ToArray());
            string html = Html(mail);
            StringAssert.Contains(html, AvisadorFichaProducto.TITULO_FOTO_TIENDA + ":");
            StringAssert.Contains(html, AvisadorFichaProducto.TITULO_FOTO_MOZO + ":");
            StringAssert.Contains(html, "src=\"cid:" + AvisadorFichaProducto.ID_FOTO_MOZO + "\"");
            Assert.IsTrue(html.IndexOf(AvisadorFichaProducto.TITULO_FOTO_TIENDA, StringComparison.Ordinal) < html.IndexOf(AvisadorFichaProducto.TITULO_FOTO_MOZO, StringComparison.Ordinal),
                "Primero la de la tienda, luego la del mozo");
        }

        [TestMethod]
        public void Correo_LasFotosLlevanElAnchoComoAtributo_ParaOutlookDeEscritorio()
        {
            // Carlos 27/11/26: Outlook de escritorio ignora max-width y la foto del mozo salía al tamaño de la cámara
            var tienda = new FotoCorreo { Bytes = new byte[] { 0xFF, 0xD8, 0xFF, 9 }, TipoMime = "image/jpeg" };

            string html = Html(AvisadorFichaProducto.Correo(AvisoCorreo(JPEG), new DatosFichaActual { Producto = "43449" }, new[] { "Foto" }, "t", null, null, tienda));

            StringAssert.Contains(html, "<img src=\"cid:" + AvisadorFichaProducto.ID_FOTO_MOZO + "\" alt=\"Foto del mozo\" width=\"320\" style=\"max-width:320px;width:100%;height:auto\"/>");
            StringAssert.Contains(html, "<img src=\"cid:" + AvisadorFichaProducto.ID_FOTO + "\" alt=\"Foto de la tienda que ha visto el mozo\" width=\"240\" style=\"max-width:240px;height:auto\"/>");
            Assert.IsFalse(html.Contains("height=\""), "Sin alto: que no se deforme");
        }

        [TestMethod]
        public void Correo_FotoDeLaTiendaSinDescargar_TambienConAncho()
        {
            string html = Html(AvisadorFichaProducto.Correo(AvisoCorreo(JPEG), new DatosFichaActual { Producto = "43449" }, new[] { "Foto" }, "t", null, null, null));

            StringAssert.Contains(html, "width=\"240\" style=\"max-width:240px;height:auto\"/>");
        }

        [TestMethod]
        public void Correo_SoloLaDelMozo_UnRecursoYLaDeLaTiendaConEnlaceYNota()
        {
            MailMessage mail = AvisadorFichaProducto.Correo(AvisoCorreo(JPEG), new DatosFichaActual { Producto = "43449" }, new[] { "Foto" }, "t", null, null, null);

            Assert.AreEqual(2, mail.AlternateViews.Count);
            Assert.AreEqual(AvisadorFichaProducto.ID_FOTO_MOZO, mail.AlternateViews.Last().LinkedResources.Single().ContentId);
            string html = Html(mail);
            StringAssert.Contains(html, AvisadorFichaProducto.TITULO_FOTO_MOZO);
            StringAssert.Contains(html, System.Net.WebUtility.HtmlEncode(AvisadorFichaProducto.NOTA_SIN_FOTO));
        }

        [TestMethod]
        public void Correo_SinNingunaFotoDeTienda_SoloLaDelMozo()
        {
            MailMessage mail = AvisadorFichaProducto.Correo(AvisoCorreo(JPEG, urlFoto: null), new DatosFichaActual { Producto = "43449" }, new[] { "Precio" }, "t", null, null, null);

            string html = Html(mail);
            StringAssert.Contains(html, AvisadorFichaProducto.TITULO_FOTO_MOZO);
            Assert.IsFalse(html.Contains(AvisadorFichaProducto.TITULO_FOTO_TIENDA));
        }

        [TestMethod]
        public void Correo_SinFotoDelMozo_ComoHoyPeroConElTituloNuevoDeLaTienda()
        {
            MailMessage mail = AvisadorFichaProducto.Correo(AvisoCorreo(null), new DatosFichaActual { Producto = "43449" }, new[] { "Foto" }, "t", null, null, null);

            Assert.AreEqual(0, mail.AlternateViews.Count);
            Assert.IsTrue(mail.IsBodyHtml);
            StringAssert.Contains(mail.Body, AvisadorFichaProducto.TITULO_FOTO_TIENDA + ":");
            Assert.IsFalse(mail.Body.Contains(AvisadorFichaProducto.TITULO_FOTO_MOZO));
        }

        [TestMethod]
        public void Correo_SinFotoDelMozo_DiceQueElMozoNoLaHaAdjuntado()
        {
            MailMessage mail = AvisadorFichaProducto.Correo(AvisoCorreo(null), new DatosFichaActual { Producto = "43449" }, new[] { "Foto" }, "t", null, null, null);

            Assert.AreEqual("El mozo no ha adjuntado foto del producto.", AvisadorFichaProducto.NOTA_SIN_FOTO_MOZO);
            StringAssert.Contains(mail.Body, AvisadorFichaProducto.NOTA_SIN_FOTO_MOZO);
            Assert.IsTrue(mail.Body.IndexOf(AvisadorFichaProducto.TITULO_FOTO_TIENDA, StringComparison.Ordinal) < mail.Body.IndexOf(AvisadorFichaProducto.NOTA_SIN_FOTO_MOZO, StringComparison.Ordinal),
                "La línea va donde iría la foto del mozo, detrás de la de la tienda");
        }

        [TestMethod]
        public void Correo_SinNingunaFoto_TambienDiceQueElMozoNoLaHaAdjuntado()
        {
            MailMessage mail = AvisadorFichaProducto.Correo(AvisoCorreo(null, urlFoto: null), new DatosFichaActual { Producto = "43449" }, new[] { "Precio" }, "t", null, null, null);

            StringAssert.Contains(mail.Body, AvisadorFichaProducto.NOTA_SIN_FOTO_MOZO);
        }

        [TestMethod]
        public void Correo_ConFotoDelMozo_NoDiceQueFalte()
        {
            MailMessage mail = AvisadorFichaProducto.Correo(AvisoCorreo(JPEG), new DatosFichaActual { Producto = "43449" }, new[] { "Foto" }, "t", null, null, null);

            Assert.IsFalse(Html(mail).Contains(AvisadorFichaProducto.NOTA_SIN_FOTO_MOZO));
        }
    }
}
