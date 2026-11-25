using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Inmovilizados;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure
{
    /// <summary>Etiquetas de inmovilizado (Carlos 25/11/26): el código «I/1823», la etiqueta, la impresora y reconocerla al leer.</summary>
    [TestClass]
    public class InmovilizadosTests
    {
        private const string USUARIO = @"NUEVAVISION\Paloma";

        private IRepositorioInmovilizados repositorio;
        private ILectorParametrosUsuario parametros;
        private IImpresoraEtiquetasAlmacen impresora;
        private List<InmovilizadoDTO> existentes;
        private List<(string impresora, TrabajoEtiquetas trabajo)> trabajos;

        private static InmovilizadoDTO Inmovilizado(string numero, string delegacion = "ALG", short estado = 0,
            string descripcion = "DEPILADOR ELÉCTRICO DEPIBLEND ref.40252 nº serie DB2024-0815")
            => new InmovilizadoDTO
            {
                Empresa = "1",
                Numero = numero,
                Codigo = CodigoInmovilizado.Componer(numero),
                Descripcion = descripcion,
                Delegacion = delegacion,
                Estado = estado,
                EstadoDescripcion = CodigoInmovilizado.DescripcionEstado(estado),
                FechaCreacion = new DateTime(2026, 11, 25)
            };

        [TestInitialize]
        public void Inicializar()
        {
            existentes = new List<InmovilizadoDTO>();
            trabajos = new List<(string, TrabajoEtiquetas)>();
            repositorio = A.Fake<IRepositorioInmovilizados>();
            A.CallTo(() => repositorio.Leer(A<string>._, A<IEnumerable<string>>._))
                .ReturnsLazily((string e, IEnumerable<string> numeros) => Task.FromResult(existentes.Where(i => numeros.Contains(i.Numero)).ToList()));
            parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametroSinDefecto(A<string>._, A<string>._, A<string>._)).Returns(null);
            A.CallTo(() => parametros.LeerParametro(A<string>._, A<string>._, ServicioEtiquetasHueco.PARAMETRO_IMPRESORA)).Returns(@"\\RDS2016\etiquetas3");
            impresora = A.Fake<IImpresoraEtiquetasAlmacen>();
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._))
                .Invokes((string i, TrabajoEtiquetas t) => trabajos.Add((i, t)))
                .Returns(LenguajeEtiquetas.Zpl);
        }

        private ServicioEtiquetasInmovilizado Servicio() => new ServicioEtiquetasInmovilizado(repositorio, parametros, impresora);

        private static IPrincipal Usuario(params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, USUARIO) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static IPrincipal ClienteTienda()
            => new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "cliente@correo.es"), new Claim("cliente", "15191") }, "prueba"));

        // ---------------------------------------------------------------- La regla del código

        [TestMethod]
        public void Componer_QuitaElRellenoDelChar()
        {
            Assert.AreEqual("I/1823", CodigoInmovilizado.Componer("1823      "));
            Assert.AreEqual("I/1823", CodigoInmovilizado.Componer("  1823"));
            Assert.IsNull(CodigoInmovilizado.Componer("   "));
        }

        [TestMethod]
        public void Leer_AdmiteMayusculasMinusculasYEspacios()
        {
            Assert.AreEqual("1823", CodigoInmovilizado.Leer("I/1823"));
            Assert.AreEqual("1823", CodigoInmovilizado.Leer("i/1823"));
            Assert.AreEqual("1823", CodigoInmovilizado.Leer("  I / 1823 "));
            Assert.AreEqual("1823", CodigoInmovilizado.Leer("I/01823"));
        }

        [TestMethod]
        public void Leer_ElNumeroASecasNoEsUnInmovilizado()
        {
            Assert.IsNull(CodigoInmovilizado.Leer("1823"), "El 1823 es la referencia de un producto");
            Assert.IsNull(CodigoInmovilizado.Leer("I1823"));
            Assert.IsNull(CodigoInmovilizado.Leer("I/"));
            Assert.IsNull(CodigoInmovilizado.Leer("I/18A3"));
            Assert.IsNull(CodigoInmovilizado.Leer("X/1823"));
            Assert.IsNull(CodigoInmovilizado.Leer("8437017506362"));
            Assert.IsNull(CodigoInmovilizado.Leer(null));
        }

        [TestMethod]
        public void DescripcionEstado_NormalYAnulado()
        {
            Assert.AreEqual("Normal", CodigoInmovilizado.DescripcionEstado(0));
            Assert.AreEqual("Anulado", CodigoInmovilizado.DescripcionEstado(-1));
        }

        [TestMethod]
        public void ADto_NumeroSinRellenoYCodigo()
        {
            InmovilizadoDTO dto = ReglasInmovilizados.ADto(new Inmovilizado
            {
                Empresa = "1  ", Número = "1823      ", Descripción = "DEPILADOR  ", Grupo = "MAQ", Delegación = "REI", Estado = -1,
                FechaCreación = new DateTime(2026, 11, 25)
            });

            Assert.AreEqual("1823", dto.Numero);
            Assert.AreEqual("I/1823", dto.Codigo);
            Assert.AreEqual("DEPILADOR", dto.Descripcion);
            Assert.AreEqual("1", dto.Empresa);
            Assert.AreEqual("Anulado", dto.EstadoDescripcion);
        }

        // ---------------------------------------------------------------- La impresora

        [TestMethod]
        public void Impresora_LaPedidaManda()
        {
            Assert.AreEqual(@"\\RDS2016\otra", ReglaImpresoraInmovilizado.Elegir(@" \\RDS2016\otra ", "ALG", _ => "x", () => "y"));
        }

        [TestMethod]
        public void Impresora_SinFilaLaDeCodigoDeCadaDelegacion()
        {
            Assert.AreEqual(@"\\RDS2016\etiquetas2", ReglaImpresoraInmovilizado.Elegir(null, "ALG", _ => null, () => throw new AssertFailedException()));
            Assert.AreEqual(@"\\RDS2016\etiquetasreina", ReglaImpresoraInmovilizado.Elegir(null, "rei", _ => "  ", () => throw new AssertFailedException()));
            Assert.AreEqual(@"\\RDS2016\etiquetasalcobendas", ReglaImpresoraInmovilizado.Elegir("", "ALC ", _ => null, () => throw new AssertFailedException()));
        }

        [TestMethod]
        public void Impresora_ConFilaLaDelParametroDeLaDelegacion()
        {
            string pedida = null;
            string resultado = ReglaImpresoraInmovilizado.Elegir(null, "REI", clave => { pedida = clave; return @"\\RDS2016\nueva "; }, () => "y");

            Assert.AreEqual(@"\\RDS2016\nueva", resultado);
            Assert.AreEqual("ImpresoraEtiquetasInmovilizado.REI", pedida);
        }

        [TestMethod]
        public void Impresora_VarUOtraLaDelUsuario()
        {
            Assert.AreEqual(@"\\RDS2016\etiquetas3", ReglaImpresoraInmovilizado.Elegir(null, "VAR", _ => throw new AssertFailedException(), () => @"\\RDS2016\etiquetas3"));
            Assert.AreEqual(@"\\RDS2016\etiquetas3", ReglaImpresoraInmovilizado.Elegir(null, null, _ => throw new AssertFailedException(), () => @"\\RDS2016\etiquetas3"));
            Assert.IsNull(ReglaImpresoraInmovilizado.ClaveParametro("VAR"));
        }

        // ---------------------------------------------------------------- La etiqueta

        [TestMethod]
        public void Componer_CabeceraCodigoDescripcionEnDosLineasYDelegacion()
        {
            EtiquetaInmovilizado e = GeneradorEtiquetasInmovilizado.Componer(Inmovilizado("1823      "));

            Assert.AreEqual("I/1823", e.Codigo);
            Assert.AreEqual("INMOVILIZADO · NO ES STOCK", e.Cabecera);
            Assert.AreEqual(2, e.Descripcion.Count);
            Assert.IsTrue(e.Descripcion.All(l => l.Length <= GeneradorEtiquetasProducto.CARACTERES_PEQUENA));
            StringAssert.StartsWith(e.Descripcion[0], "DEPILADOR ELÉCTRICO");
            Assert.AreEqual("ALG", e.Delegacion);
        }

        [TestMethod]
        public void Zpl_LlevaElCode128ConElTextoExacto()
        {
            EtiquetaInmovilizado e = GeneradorEtiquetasInmovilizado.Componer(Inmovilizado("1823"));

            string zpl = GeneradorEtiquetasInmovilizado.Zpl(new[] { e }, 2);

            StringAssert.Contains(zpl, "^BCN,64,N,N,N,A^FH^FDI/1823^FS");
            StringAssert.Contains(zpl, "^FDI/1823^FS");
            StringAssert.Contains(zpl, "INMOVILIZADO _C2_B7 NO ES STOCK");
            StringAssert.Contains(zpl, "^FDALG^FS");
            StringAssert.Contains(zpl, "^PQ2^XZ");
            StringAssert.StartsWith(zpl, "^XA");
        }

        [TestMethod]
        public void Epl_LlevaElCode128ConElTextoExacto()
        {
            EtiquetaInmovilizado e = GeneradorEtiquetasInmovilizado.Componer(Inmovilizado("1823"));

            string epl = GeneradorEtiquetasInmovilizado.Epl(new[] { e }, 1);

            StringAssert.Contains(epl, $"B{GeneradorEtiquetasInmovilizado.BarrasX("I/1823")},80,0,1,2,2,64,N,\"I/1823\"");
            StringAssert.Contains(epl, "\"INMOVILIZADO · NO ES STOCK\"");
            StringAssert.Contains(epl, "\"I/1823\"\r\n");
            StringAssert.Contains(epl, "P1\r\n");
        }

        [TestMethod]
        public void Code128_CabeYQuedaCentrado()
        {
            Assert.AreEqual(90, GeneradorEtiquetasInmovilizado.ModulosCode128("I/1823"));
            Assert.AreEqual(2, GeneradorEtiquetasInmovilizado.Estrecho("I/1823"));
            Assert.AreEqual(30, GeneradorEtiquetasInmovilizado.BarrasX("I/1823"));
            // Uno de 5 cifras: la impar en B y el resto en C
            Assert.AreEqual(101, GeneradorEtiquetasInmovilizado.ModulosCode128("I/12345"));
            Assert.IsTrue(GeneradorEtiquetasInmovilizado.BarrasX("I/12345") >= 0);
        }

        // ---------------------------------------------------------------- Imprimir

        [TestMethod]
        public async Task Imprimir_UnTrabajoPorImpresoraDeLaDelegacion()
        {
            existentes.Add(Inmovilizado("1823", "ALG"));
            existentes.Add(Inmovilizado("1822", "REI"));
            existentes.Add(Inmovilizado("1821", "ALG"));

            ResultadoEtiquetasInmovilizadoDTO r = await Servicio().Imprimir(
                new EtiquetasInmovilizadoDTO { Numeros = new List<string> { "1823", "I/1822", "1821" }, Copias = 2 }, USUARIO);

            Assert.AreEqual(6, r.Impresas);
            Assert.AreEqual(3, r.Etiquetas.Count);
            Assert.IsTrue(r.Etiquetas.All(e => e.Ok && e.Motivo == null));
            Assert.AreEqual(2, trabajos.Count);
            Assert.AreEqual(@"\\RDS2016\etiquetas2", r.Etiquetas.Single(e => e.Numero == "1823").Impresora);
            Assert.AreEqual(@"\\RDS2016\etiquetasreina", r.Etiquetas.Single(e => e.Numero == "1822").Impresora);
            string zplAlgete = trabajos.Single(t => t.impresora == @"\\RDS2016\etiquetas2").trabajo.Zpl;
            StringAssert.Contains(zplAlgete, "^FDI/1823^FS");
            StringAssert.Contains(zplAlgete, "^FDI/1821^FS");
            // Lee la fila «(defecto)» sin crear ninguna (nunca LeerParametro con la clave de la delegación)
            A.CallTo(() => parametros.LeerParametroSinDefecto("1", "(defecto)", "ImpresoraEtiquetasInmovilizado.ALG")).MustHaveHappenedOnceExactly();
            A.CallTo(() => parametros.LeerParametro(A<string>._, A<string>._, A<string>.That.StartsWith("ImpresoraEtiquetasInmovilizado"))).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Imprimir_ElQueNoExisteOAnuladoVaEnSuFilaSinPararALosDemas()
        {
            existentes.Add(Inmovilizado("1823", "ALG"));
            existentes.Add(Inmovilizado("1800", "ALG", estado: -1));

            ResultadoEtiquetasInmovilizadoDTO r = await Servicio().Imprimir(
                new EtiquetasInmovilizadoDTO { Numeros = new List<string> { "1823", "9999", "1800", "abc" } }, USUARIO);

            Assert.AreEqual(1, r.Impresas);
            Assert.IsTrue(r.Etiquetas.Single(e => e.Numero == "1823").Ok);
            EtiquetaInmovilizadoResultadoDTO noExiste = r.Etiquetas.Single(e => e.Numero == "9999");
            Assert.IsFalse(noExiste.Ok);
            Assert.AreEqual("No existe el inmovilizado I/9999.", noExiste.Motivo);
            StringAssert.Contains(r.Etiquetas.Single(e => e.Numero == "1800").Motivo, "anulado");
            Assert.IsFalse(r.Etiquetas.Single(e => e.Numero == "abc").Ok);
        }

        [TestMethod]
        public async Task Imprimir_NingunoExiste_Null()
        {
            Assert.IsNull(await Servicio().Imprimir(new EtiquetasInmovilizadoDTO { Numeros = new List<string> { "9999" } }, USUARIO));
            A.CallTo(() => impresora.Imprimir(A<string>._, A<TrabajoEtiquetas>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Imprimir_UnaImpresoraFallaYLaOtraSigue()
        {
            existentes.Add(Inmovilizado("1823", "ALG"));
            existentes.Add(Inmovilizado("1822", "REI"));
            A.CallTo(() => impresora.Imprimir(@"\\RDS2016\etiquetasreina", A<TrabajoEtiquetas>._))
                .Throws(new ImpresionEtiquetasException("No se puede abrir la impresora \\\\RDS2016\\etiquetasreina (error de Windows 5)."));

            ResultadoEtiquetasInmovilizadoDTO r = await Servicio().Imprimir(
                new EtiquetasInmovilizadoDTO { Numeros = new List<string> { "1823", "1822" } }, USUARIO);

            Assert.AreEqual(1, r.Impresas);
            Assert.IsTrue(r.Etiquetas.Single(e => e.Numero == "1823").Ok);
            StringAssert.Contains(r.Etiquetas.Single(e => e.Numero == "1822").Motivo, "error de Windows 5");
        }

        [TestMethod]
        public async Task Imprimir_VarVaALaImpresoraDelUsuarioYLaPedidaATodas()
        {
            existentes.Add(Inmovilizado("1823", "VAR"));
            ResultadoEtiquetasInmovilizadoDTO r = await Servicio().Imprimir(new EtiquetasInmovilizadoDTO { Numeros = new List<string> { "1823" } }, USUARIO);
            Assert.AreEqual(@"\\RDS2016\etiquetas3", r.Etiquetas.Single().Impresora);

            existentes.Add(Inmovilizado("1822", "REI"));
            r = await Servicio().Imprimir(new EtiquetasInmovilizadoDTO { Numeros = new List<string> { "1823", "1822" }, Impresora = @"\\RDS2016\pruebas" }, USUARIO);
            Assert.IsTrue(r.Etiquetas.All(e => e.Impresora == @"\\RDS2016\pruebas"));
        }

        [TestMethod]
        public async Task Imprimir_CopiasONumerosDeMas_ArgumentException()
        {
            existentes.Add(Inmovilizado("1823"));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                Servicio().Imprimir(new EtiquetasInmovilizadoDTO { Numeros = new List<string> { "1823" }, Copias = 11 }, USUARIO));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                Servicio().Imprimir(new EtiquetasInmovilizadoDTO { Numeros = Enumerable.Range(1, 51).Select(n => n.ToString()).ToList() }, USUARIO));
            await Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                Servicio().Imprimir(new EtiquetasInmovilizadoDTO(), USUARIO));
        }

        // ---------------------------------------------------------------- Reconocerla al leer

        [TestMethod]
        public async Task Reconocer_NumeroASecas_NullSinLeerNada()
        {
            Assert.IsNull(await ReglasInmovilizados.Reconocer(repositorio, "1", "1823"));
            Assert.IsNull(await ReglasInmovilizados.Reconocer(repositorio, "1", "8437017506362"));
            A.CallTo(() => repositorio.Leer(A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Reconocer_ExisteYNoExiste()
        {
            existentes.Add(Inmovilizado("1823", descripcion: "DEPILADOR ELÉCTRICO DEPIBLEND ref.40252"));

            EsInmovilizadoDTO si = await ReglasInmovilizados.Reconocer(repositorio, null, "i/1823");
            EsInmovilizadoDTO no = await ReglasInmovilizados.Reconocer(repositorio, "1", "I/9999");

            Assert.AreEqual("ES_INMOVILIZADO", si.Codigo);
            Assert.AreEqual("I/1823 es un inmovilizado (DEPILADOR ELÉCTRICO DEPIBLEND ref.40252): no es stock, no se cuenta ni se prepara.", si.Message);
            Assert.AreEqual("1823", si.Inmovilizado.Numero);
            Assert.AreEqual(0, si.Productos.Count);
            Assert.AreEqual("ES_INMOVILIZADO", no.Codigo);
            Assert.AreEqual("I/9999 tiene formato de inmovilizado, pero no existe.", no.Message);
            Assert.IsNull(no.Inmovilizado);
        }

        [TestMethod]
        public void EsInmovilizado_AnuladoLoDice()
        {
            StringAssert.Contains(ReglasInmovilizados.EsInmovilizado("1800", Inmovilizado("1800", estado: -1, descripcion: "CAMILLA")).Message,
                "I/1800 es un inmovilizado (CAMILLA, anulado)");
        }

        [TestMethod]
        public async Task ProductosPorCodigoBarras_Inmovilizado_404ConElMotivo()
        {
            existentes.Add(Inmovilizado("1823"));
            var controller = new ProductosController(A.Fake<NVEntities>(), A.Fake<IGestorSincronizacion>()) { Inmovilizados = repositorio };

            var respuesta = await controller.GetProducto("I/1823") as NegotiatedContentResult<EsInmovilizadoDTO>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(HttpStatusCode.NotFound, respuesta.StatusCode);
            Assert.AreEqual("ES_INMOVILIZADO", respuesta.Content.Codigo);
        }

        [TestMethod]
        public async Task Inventarios_LeerOCrearUnInmovilizado_409ConElMotivo()
        {
            existentes.Add(Inmovilizado("1823"));
            var controller = new InventariosController(A.Fake<NVEntities>(), A.Fake<IRepositorioCodigosBarras>()) { Inmovilizados = repositorio };

            var leer = await controller.GetInventario("1", "ALC", new DateTime(2026, 11, 25), "I/1823") as NegotiatedContentResult<EsInmovilizadoDTO>;
            var crear = await controller.PostInventario(new Inventario { Empresa = "1", Almacén = "ALC", Número = "I/9999", Fecha = new DateTime(2026, 11, 25) })
                as NegotiatedContentResult<EsInmovilizadoDTO>;

            Assert.AreEqual(HttpStatusCode.Conflict, leer.StatusCode);
            StringAssert.Contains(leer.Content.Message, "no se cuenta");
            Assert.AreEqual(HttpStatusCode.Conflict, crear.StatusCode);
            Assert.AreEqual("I/9999 tiene formato de inmovilizado, pero no existe.", crear.Content.Message);
        }

        [TestMethod]
        public async Task FichaDeAriadna_Inmovilizado_404ConElMotivo_TambienConLaBarraPartida()
        {
            existentes.Add(Inmovilizado("1823"));
            var ficha = A.Fake<IServicioFichaProductoAlmacen>();
            var controller = new AlmacenController(A.Fake<IServicioPreparacionAlmacen>(), A.Fake<IServicioUbicacionesAlmacen>(),
                A.Fake<IServicioRecepcionCompras>(), A.Fake<IServicioRecepcionReposiciones>(), fichaProducto: ficha)
            {
                Request = new HttpRequestMessage(),
                Inmovilizados = repositorio
            };

            var entera = await controller.GetFichaProducto("I/1823") as NegotiatedContentResult<EsInmovilizadoDTO>;
            var partida = await controller.GetFichaInmovilizado("1823") as NegotiatedContentResult<EsInmovilizadoDTO>;

            Assert.AreEqual(HttpStatusCode.NotFound, entera.StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, partida.StatusCode);
            Assert.AreEqual("I/1823", partida.Content.Inmovilizado.Codigo);
            A.CallTo(() => ficha.Leer(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- El controlador

        private InmovilizadosController Controlador(IPrincipal usuario, IServicioEtiquetasInmovilizado servicio = null)
            => new InmovilizadosController(repositorio, servicio ?? A.Fake<IServicioEtiquetasInmovilizado>())
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = usuario
            };

        [TestMethod]
        public async Task Get_BuscaComoMucho500()
        {
            A.CallTo(() => repositorio.Buscar("1", "I/1823", "ALG", true, 500)).Returns(new List<InmovilizadoDTO> { Inmovilizado("1823") });

            var respuesta = await Controlador(Usuario("Comerciales")).GetInmovilizados("1", "I/1823", "ALG") as OkNegotiatedContentResult<List<InmovilizadoDTO>>;

            Assert.AreEqual("I/1823", respuesta.Content.Single().Codigo);
        }

        [TestMethod]
        public async Task Get_ClienteDeLaTienda_403()
        {
            var respuesta = await Controlador(ClienteTienda()).GetInmovilizados() as NegotiatedContentResult<HttpError>;

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            A.CallTo(() => repositorio.Buscar(A<string>._, A<string>._, A<string>._, A<bool>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Imprimir_LosGruposQuePueden()
        {
            foreach (string grupo in new[] { "Administración", "Dirección", "Almacén", "Tiendas", "Informática" })
            {
                var servicio = A.Fake<IServicioEtiquetasInmovilizado>();
                A.CallTo(() => servicio.Imprimir(A<EtiquetasInmovilizadoDTO>._, A<string>._)).Returns(new ResultadoEtiquetasInmovilizadoDTO { Impresas = 1 });

                IHttpActionResult respuesta = await Controlador(Usuario(grupo), servicio).PostImprimirEtiquetas(new EtiquetasInmovilizadoDTO());

                Assert.IsInstanceOfType(respuesta, typeof(OkNegotiatedContentResult<ResultadoEtiquetasInmovilizadoDTO>), grupo);
            }
        }

        [TestMethod]
        public async Task Imprimir_SinGrupo_403DeUsted()
        {
            var servicio = A.Fake<IServicioEtiquetasInmovilizado>();

            var respuesta = await Controlador(Usuario("Comerciales"), servicio).PostImprimirEtiquetas(new EtiquetasInmovilizadoDTO()) as NegotiatedContentResult<HttpError>;

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.AreEqual(ReglasInmovilizados.MENSAJE_SIN_PERMISO_ETIQUETAS, respuesta.Content.Message);
            A.CallTo(() => servicio.Imprimir(A<EtiquetasInmovilizadoDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Imprimir_NingunoExiste404_Validacion400()
        {
            var servicio = A.Fake<IServicioEtiquetasInmovilizado>();
            A.CallTo(() => servicio.Imprimir(A<EtiquetasInmovilizadoDTO>._, A<string>._)).Returns((ResultadoEtiquetasInmovilizadoDTO)null);
            var noExiste = await Controlador(Usuario("Almacén"), servicio).PostImprimirEtiquetas(new EtiquetasInmovilizadoDTO()) as NegotiatedContentResult<HttpError>;

            A.CallTo(() => servicio.Imprimir(A<EtiquetasInmovilizadoDTO>._, A<string>._)).ThrowsAsync(new ArgumentException("Como mucho…"));
            var mal = await Controlador(Usuario("Almacén"), servicio).PostImprimirEtiquetas(new EtiquetasInmovilizadoDTO()) as NegotiatedContentResult<HttpError>;

            Assert.AreEqual(HttpStatusCode.NotFound, noExiste.StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, mal.StatusCode);
        }
    }
}
