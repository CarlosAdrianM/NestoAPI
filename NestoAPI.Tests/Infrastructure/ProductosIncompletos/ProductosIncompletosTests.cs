using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.AlmacenesFicticios;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.ExtractosProducto;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.ProductosIncompletos;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.ProductosIncompletos
{
    /// <summary>
    /// NestoAPI#902: productos incompletos (sacar una pieza de una unidad sin que sea un inventario). Las reglas puras (validaciones,
    /// número de serie, permisos, almacén de incompletos, la unidad, los apuntes, la etiqueta), el servicio con el repositorio falseado
    /// y el controlador.
    /// </summary>
    [TestClass]
    public class ProductosIncompletosTests
    {
        private static SacarPiezaDTO Peticion(string almacen = "ALG", string serie = "101270") => new SacarPiezaDTO
        {
            Almacen = almacen,
            ProductoDonante = "40252",
            NumeroSerie = serie,
            Pieza = "43415",
            Cantidad = 1,
            Pedido = 931421
        };

        private static ProductoIncompletoDTO Ficha(int id = 7, string serie = "101270", string estado = EstadosProductoIncompleto.INCOMPLETO,
            string almacen = "ALG") => new ProductoIncompletoDTO
            {
                Id = id,
                Empresa = "1",
                Almacen = almacen,
                AlmacenIncompletos = "INC",
                ProductoDonante = "40252",
                NombreDonante = "DEPIBLEND",
                NumeroSerie = serie,
                Pieza = "43415",
                NombrePieza = "CABLE PORTAAGUJAS",
                Cantidad = 1,
                Pedido = 931421,
                Estado = estado,
                Fecha = new DateTime(2026, 11, 24, 13, 14, 0),
                PvpDonante = 1500m
            };

        // ---------------------------------------------------------------- Validaciones de la petición

        [TestMethod]
        public void MotivoPeticionNoValida_LaBuena_Null()
        {
            Assert.IsNull(ReglasProductosIncompletos.MotivoPeticionNoValida(Peticion()));
        }

        [TestMethod]
        public void MotivoPeticionNoValida_LosCasosQueNoValen()
        {
            Assert.IsNotNull(ReglasProductosIncompletos.MotivoPeticionNoValida(null));

            SacarPiezaDTO p = Peticion("INC");
            StringAssert.Contains(ReglasProductosIncompletos.MotivoPeticionNoValida(p), "solo ALG, REI o ALC");

            p = Peticion();
            p.Pieza = "40252";
            StringAssert.Contains(ReglasProductosIncompletos.MotivoPeticionNoValida(p), "mismo producto");

            p = Peticion();
            p.Cantidad = 0;
            StringAssert.Contains(ReglasProductosIncompletos.MotivoPeticionNoValida(p), "entre 1 y");

            p = Peticion();
            p.Pasillo = "002";
            StringAssert.Contains(ReglasProductosIncompletos.MotivoPeticionNoValida(p), "pasillo, la fila y la columna");

            p = Peticion();
            p.HuecoDestino = "12A";
            StringAssert.Contains(ReglasProductosIncompletos.MotivoPeticionNoValida(p), "9 cifras");

            p = Peticion();
            p.Pieza = " ";
            StringAssert.Contains(ReglasProductosIncompletos.MotivoPeticionNoValida(p), "qué pieza");

            p = Peticion();
            p.Pedido = 0;
            Assert.IsNotNull(ReglasProductosIncompletos.MotivoPeticionNoValida(p));
        }

        [TestMethod]
        public void MotivoPeticionNoValida_ElHuecoDestinoConBarrasVale()
        {
            SacarPiezaDTO p = Peticion();
            p.HuecoDestino = "050/001/002";
            Assert.IsNull(ReglasProductosIncompletos.MotivoPeticionNoValida(p));
        }

        // ---------------------------------------------------------------- LA REGLA del número de serie

        [TestMethod]
        public void NumeroSerie_ObligatorioSiElDonanteLoNecesita()
        {
            var depiblend = new ProductoIncompletoLeido { Producto = "40252", Nombre = "DEPIBLEND", NecesitaNumSerie = true };
            StringAssert.Contains(ReglasProductosIncompletos.MotivoNumeroSerie(depiblend, null), "lleva número de serie");
            StringAssert.Contains(ReglasProductosIncompletos.MotivoNumeroSerie(depiblend, "  "), "léalo o tecléelo");
            Assert.IsNull(ReglasProductosIncompletos.MotivoNumeroSerie(depiblend, "101270"));

            var crema = new ProductoIncompletoLeido { Producto = "45958", NecesitaNumSerie = false };
            Assert.IsNull(ReglasProductosIncompletos.MotivoNumeroSerie(crema, null));
        }

        [TestMethod]
        public void MotivosProductos_NoExisteOEsFicticio()
        {
            List<string> motivos = ReglasProductosIncompletos.MotivosProductos(Peticion(), null,
                new ProductoIncompletoLeido { Producto = "43415", Ficticio = true });
            Assert.AreEqual(2, motivos.Count);
            StringAssert.Contains(motivos[0], "40252 no existe");
            StringAssert.Contains(motivos[1], "ficticio");
        }

        // ---------------------------------------------------------------- Permisos

        [TestMethod]
        public void Permisos_AlmacenDireccionInformaticaEnCualquierSede_TiendasSoloLaSuya_ComprasNo()
        {
            Assert.IsNull(ReglasProductosIncompletos.MotivoNoPuedeOperar(new ContextoUsuarioIncompletos { EsAlmacen = true }, "REI"));
            Assert.IsNull(ReglasProductosIncompletos.MotivoNoPuedeOperar(new ContextoUsuarioIncompletos { EsDireccion = true }, "ALG"));
            Assert.IsNull(ReglasProductosIncompletos.MotivoNoPuedeOperar(new ContextoUsuarioIncompletos { EsInformatica = true }, "ALC"));

            var reina = new ContextoUsuarioIncompletos { EsTiendas = true, AlmacenTienda = "REI" };
            Assert.IsNull(ReglasProductosIncompletos.MotivoNoPuedeOperar(reina, "rei"));
            StringAssert.Contains(ReglasProductosIncompletos.MotivoNoPuedeOperar(reina, "ALG"), "solo puede hacerlo con lo de REI");
            StringAssert.Contains(ReglasProductosIncompletos.MotivoNoPuedeOperar(new ContextoUsuarioIncompletos { EsTiendas = true }, "REI"), "AlmacénPedidoVta");

            var compras = new ContextoUsuarioIncompletos { EsCompras = true };
            Assert.IsNotNull(ReglasProductosIncompletos.MotivoNoPuedeOperar(compras, "ALG"));
            Assert.IsTrue(ReglasProductosIncompletos.PuedeConsultar(compras));
            Assert.IsFalse(ReglasProductosIncompletos.PuedeConsultar(new ContextoUsuarioIncompletos()));
        }

        [TestMethod]
        public void AlmacenVisible_LasTiendasSoloLoSuyo()
        {
            Assert.AreEqual("REI", ReglasProductosIncompletos.AlmacenVisible(new ContextoUsuarioIncompletos { EsTiendas = true, AlmacenTienda = "REI" }, "ALG"));
            Assert.AreEqual("ALG", ReglasProductosIncompletos.AlmacenVisible(new ContextoUsuarioIncompletos { EsCompras = true }, "alg"));
            Assert.IsNull(ReglasProductosIncompletos.AlmacenVisible(new ContextoUsuarioIncompletos { EsAlmacen = true }, null));
        }

        // ---------------------------------------------------------------- El almacén de incompletos

        private static readonly List<AlmacenLeido> ALMACENES = new List<AlmacenLeido>
        {
            new AlmacenLeido { Codigo = "ALG", ControlUbicaciones = true },
            new AlmacenLeido { Codigo = "REI" },
            new AlmacenLeido { Codigo = "ALC" },
            new AlmacenLeido { Codigo = "INC", EsFicticio = true },
            new AlmacenLeido { Codigo = "UBI", EsFicticio = true, ControlUbicaciones = true },
            new AlmacenLeido { Codigo = "NOF" }
        };

        [TestMethod]
        public void AlmacenIncompletos_NoPuedeSerUnaSede_NiFaltar_NiLlevarUbicaciones()
        {
            Assert.IsNull(ReglasProductosIncompletos.MotivoAlmacenIncompletosNoValido("inc", ALMACENES));
            StringAssert.Contains(ReglasProductosIncompletos.MotivoAlmacenIncompletosNoValido(null, ALMACENES), "AlmacenIncompletos");
            StringAssert.Contains(ReglasProductosIncompletos.MotivoAlmacenIncompletosNoValido("ALG", ALMACENES), "contarían como stock disponible");
            StringAssert.Contains(ReglasProductosIncompletos.MotivoAlmacenIncompletosNoValido("XYZ", ALMACENES), "no existe");
            StringAssert.Contains(ReglasProductosIncompletos.MotivoAlmacenIncompletosNoValido("UBI", ALMACENES), "control de ubicaciones");
            StringAssert.Contains(ReglasProductosIncompletos.MotivoAlmacenIncompletosNoValido("NOF", ALMACENES), "tiene que ser un almacén ficticio");
        }

        [TestMethod]
        public void LasSedesSonLasQueCuentanComoStock()
        {
            // La prueba de que el almacén de incompletos NO cuenta: ServicioGestorStocks, el motor de fechas (StockSedes) y el disparador
            // de la tienda (#818) solo suman Constantes.Sedes.ListaSedes
            CollectionAssert.AreEquivalent(new[] { "ALG", "REI", "ALC" }, Constantes.Sedes.ListaSedes);
            Assert.IsFalse(ReglasProductosIncompletos.EsSede("INC"));
        }

        // ---------------------------------------------------------------- La unidad

        [TestMethod]
        public void MismaUnidad_SoloConElMismoNumeroDeSerie()
        {
            Assert.IsTrue(ReglasProductosIncompletos.MismaUnidad(Ficha(), "40252", " 101270 "));
            Assert.IsFalse(ReglasProductosIncompletos.MismaUnidad(Ficha(), "40252", "101268"));
            Assert.IsFalse(ReglasProductosIncompletos.MismaUnidad(Ficha(serie: null), "40252", null), "sin número de serie cada ficha es su unidad");
            Assert.IsNull(ReglasProductosIncompletos.UnidadYaApartada(new[] { Ficha(estado: EstadosProductoIncompleto.COMPLETADO) }, "40252", "101270"));
            Assert.AreEqual(7, ReglasProductosIncompletos.UnidadYaApartada(new[] { Ficha() }, "40252", "101270").Id);
        }

        [TestMethod]
        public void OtrasDeLaUnidad_NoCuentaLaPropia()
        {
            List<ProductoIncompletoDTO> vivas = new List<ProductoIncompletoDTO> { Ficha(7), Ficha(8), Ficha(9, serie: "101268") };
            CollectionAssert.AreEqual(new[] { 8 }, ReglasProductosIncompletos.OtrasDeLaUnidad(Ficha(7), vivas).Select(f => f.Id).ToList());
            Assert.AreEqual(0, ReglasProductosIncompletos.OtrasDeLaUnidad(Ficha(9, serie: "101268"), vivas).Count);
        }

        [TestMethod]
        public void MotivoUnidadEnOtroAlmacen()
        {
            Assert.IsNull(ReglasProductosIncompletos.MotivoUnidadEnOtroAlmacen(Ficha(), "ALG"));
            StringAssert.Contains(ReglasProductosIncompletos.MotivoUnidadEnOtroAlmacen(Ficha(), "REI"), "ya está incompleta en ALG");
        }

        [TestMethod]
        public void MotivoNoSePuedeCerrar_SoloLoIncompleto()
        {
            Assert.IsNull(ReglasProductosIncompletos.MotivoNoSePuedeCerrar(Ficha()));
            StringAssert.Contains(ReglasProductosIncompletos.MotivoNoSePuedeCerrar(Ficha(estado: EstadosProductoIncompleto.COMPLETADO)), "ya se completó");
            StringAssert.Contains(ReglasProductosIncompletos.MotivoNoSePuedeCerrar(Ficha(estado: EstadosProductoIncompleto.BAJA)), "ya se dio de baja");
        }

        // ---------------------------------------------------------------- Los apuntes

        [TestMethod]
        public void LineasSacar_LaPiezaEntraYLaUnidadPasaAIncompletos()
        {
            List<LineaMovimientoTraspaso> lineas = ReglasProductosIncompletos.LineasSacar("alg", "INC", "40252", "101270", "43415", 2, moverUnidad: true);
            Assert.AreEqual(3, lineas.Count);
            Assert.AreEqual("ALG", lineas[0].Almacen);
            Assert.AreEqual("43415", lineas[0].Producto);
            Assert.AreEqual(2, lineas[0].Cantidad);
            Assert.AreEqual(-1, lineas.Single(l => l.Almacen == "ALG" && l.Producto == "40252").Cantidad);
            Assert.AreEqual(1, lineas.Single(l => l.Almacen == "INC" && l.Producto == "40252").Cantidad);
            Assert.IsTrue(lineas.All(l => l.Texto.Length <= 50));
            StringAssert.Contains(lineas[0].Texto, "NS 101270");

            Assert.AreEqual(1, ReglasProductosIncompletos.LineasSacar("ALG", "INC", "40252", "101270", "43415", 1, moverUnidad: false).Count);
        }

        [TestMethod]
        public void LineasCompletarYBaja()
        {
            List<LineaMovimientoTraspaso> completar = ReglasProductosIncompletos.LineasCompletar(Ficha(), vuelveUnidad: true);
            Assert.AreEqual(-1, completar.Single(l => l.Producto == "43415" && l.Almacen == "ALG").Cantidad);
            Assert.AreEqual(-1, completar.Single(l => l.Producto == "40252" && l.Almacen == "INC").Cantidad);
            Assert.AreEqual(1, completar.Single(l => l.Producto == "40252" && l.Almacen == "ALG").Cantidad);
            Assert.AreEqual(1, ReglasProductosIncompletos.LineasCompletar(Ficha(), vuelveUnidad: false).Count);

            LineaMovimientoTraspaso baja = ReglasProductosIncompletos.LineasBaja(Ficha(), "Se ha roto la carcasa").Single();
            Assert.AreEqual("INC", baja.Almacen);
            Assert.AreEqual(-1, baja.Cantidad);
            StringAssert.Contains(baja.Texto, "Se ha roto");
        }

        [TestMethod]
        public void MotivoSinStock()
        {
            Assert.IsNull(ReglasProductosIncompletos.MotivoSinStock("40252", "ALG", 1, new Dictionary<string, int> { ["40252"] = 3 }, false, "la unidad"));
            StringAssert.Contains(ReglasProductosIncompletos.MotivoSinStock("40252", "ALG", 1, new Dictionary<string, int>(), false, "la unidad"), "hay 0 de 40252");
            Assert.IsNull(ReglasProductosIncompletos.MotivoSinStock("40252", "ALG", 1, new Dictionary<string, int>(), true, "la unidad"));
        }

        // ---------------------------------------------------------------- Listado, textos y etiqueta

        [TestMethod]
        public void Listado_DosPiezasDeLaMismaSerieSonUnaUnidad()
        {
            ListadoProductosIncompletosDTO listado = ReglasProductosIncompletos.Listado(new[]
            {
                Ficha(7), Ficha(8), Ficha(9, serie: "101268"), Ficha(10, estado: EstadosProductoIncompleto.COMPLETADO)
            }, new DateTime(2026, 11, 26));
            Assert.AreEqual(4, listado.Total);
            Assert.AreEqual(2, listado.UnidadesIncompletas);
            Assert.AreEqual(3000m, listado.ValorPvp);
            Assert.AreEqual(2, listado.Fichas[0].Dias);
            StringAssert.Contains(listado.Fichas[0].Donde, "En INC");
            Assert.AreEqual(10, listado.Fichas.Last().Id, "las cerradas, al final");
        }

        [TestMethod]
        public void NotificacionCompras_DiceQueSeDebe()
        {
            NotificacionPushDTO n = ReglasProductosIncompletos.NotificacionCompras(Ficha());
            Assert.AreEqual("ProductoIncompleto", n.Tipo);
            StringAssert.StartsWith(n.Cuerpo, "Se debe 1 ud. de 43415 CABLE PORTAAGUJAS al 40252 DEPIBLEND nº de serie 101270 (ALG, ficha 7).");
            StringAssert.Contains(n.Cuerpo, "pedido 931421");
            Assert.AreEqual("7", n.Datos["id"]);
        }

        [TestMethod]
        public void Etiqueta_LlevaIncompletoProductoSeriePiezaFechaPedidoYElCodigo()
        {
            List<string> lineas = GeneradorEtiquetaIncompleto.Lineas(Ficha());
            Assert.AreEqual("Ref. 40252 NS 101270", lineas[0]);
            Assert.AreEqual("DEPIBLEND", lineas[1]);
            StringAssert.StartsWith(lineas[2], "Falta: 1 43415 CABLE");
            Assert.AreEqual("24/11/26 · Pedido 931421", lineas[3]);
            Assert.IsTrue(lineas.All(l => l.Length <= GeneradorEtiquetasProducto.CARACTERES_PEQUENA));

            string epl = GeneradorEtiquetaIncompleto.Epl(Ficha());
            StringAssert.Contains(epl, "\"INCOMPLETO\"");
            StringAssert.Contains(epl, "\"INC7\"");
            string zpl = GeneradorEtiquetaIncompleto.Zpl(Ficha());
            StringAssert.Contains(zpl, "^FDINC7^FS");
            StringAssert.Contains(zpl, "INCOMPLETO");
        }

        [TestMethod]
        public void ImpresoraEtiqueta_PorLaImpresoraDelUsuario_YSinImpresoraNoRompe()
        {
            ILectorParametrosUsuario parametros = A.Fake<ILectorParametrosUsuario>();
            IImpresoraEtiquetasAlmacen impresora = A.Fake<IImpresoraEtiquetasAlmacen>();
            A.CallTo(() => parametros.LeerParametro("1", "Santi", "ImpresoraCodBarras")).Returns("\\\\ALG\\ZEBRA");
            var servicio = new ImpresoraEtiquetaIncompleto(() => parametros, () => impresora);

            EtiquetaIncompletoDTO e = servicio.Imprimir(Ficha(), "NUEVAVISION\\Santi", false);
            Assert.IsTrue(e.Impresa);
            Assert.AreEqual("INC7", e.Codigo);
            A.CallTo(() => impresora.Imprimir("\\\\ALG\\ZEBRA", A<TrabajoEtiquetas>._)).MustHaveHappenedOnceExactly();

            EtiquetaIncompletoDTO sin = servicio.Imprimir(Ficha(), "NUEVAVISION\\Otro", false);
            Assert.IsFalse(sin.Impresa);
            StringAssert.Contains(sin.Mensaje, "No se ha podido imprimir");
        }

        [TestMethod]
        public async Task Avisador_ACompras_YSinNadieNoRompe()
        {
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var avisador = new AvisadorProductoIncompleto(() => push, g => g == "Compras" ? new List<string> { "Marta", "(Grupo)" } : new List<string>());
            Assert.IsTrue(await avisador.AvisarACompras(ReglasProductosIncompletos.NotificacionCompras(Ficha())));
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Marta", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();

            var nadie = new AvisadorProductoIncompleto(() => push, g => new List<string>());
            Assert.IsFalse(await nadie.AvisarACompras(ReglasProductosIncompletos.NotificacionCompras(Ficha())));
        }

        // ---------------------------------------------------------------- El servicio con el repositorio falseado

        private IRepositorioProductosIncompletos repositorio;
        private ITransaccionProductosIncompletos transaccion;
        private IAvisadorProductoIncompleto avisador;
        private IImpresoraEtiquetaIncompleto impresora;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioProductosIncompletos>();
            transaccion = A.Fake<ITransaccionProductosIncompletos>();
            avisador = A.Fake<IAvisadorProductoIncompleto>();
            impresora = A.Fake<IImpresoraEtiquetaIncompleto>();
            A.CallTo(() => repositorio.TablaCreada()).Returns(Task.FromResult(true));
            A.CallTo(() => repositorio.LeerAlmacenIncompletos()).Returns(Task.FromResult("INC"));
            A.CallTo(() => repositorio.LeerAlmacenes()).Returns(Task.FromResult(ALMACENES.ToList()));
            A.CallTo(() => repositorio.LeerProductos(A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, ProductoIncompletoLeido>(StringComparer.OrdinalIgnoreCase)
            {
                ["40252"] = new ProductoIncompletoLeido { Producto = "40252", Nombre = "DEPIBLEND", Ubicar = true, NecesitaNumSerie = true, Pvp = 1500m },
                ["43415"] = new ProductoIncompletoLeido { Producto = "43415", Nombre = "CABLE PORTAAGUJAS", Ubicar = true }
            }));
            A.CallTo(() => repositorio.LeerFichas(A<bool>._, A<string>._, A<string>._)).Returns(Task.FromResult(new List<ProductoIncompletoDTO>()));
            A.CallTo(() => repositorio.LeerStock(A<string>._, A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["40252"] = 3, ["43415"] = 1 }));
            A.CallTo(() => repositorio.LeerHuecosLibres("ALG", A<IEnumerable<string>>._)).Returns(Task.FromResult(new List<HuecoLibreFicticio>
            {
                new HuecoLibreFicticio { Producto = "40252", Pasillo = "012", Fila = "003", Columna = "001", Libres = 3, Desde = new DateTime(2026, 10, 1) },
                new HuecoLibreFicticio { Producto = "43415", Pasillo = "020", Fila = "001", Columna = "001", Libres = 1, Desde = new DateTime(2026, 10, 1) }
            }));
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionProductosIncompletos, Task<ResultadoProductoIncompletoDTO>>>._))
                .ReturnsLazily((Func<ITransaccionProductosIncompletos, Task<ResultadoProductoIncompletoDTO>> trabajo) => trabajo(transaccion));
            A.CallTo(() => transaccion.LeerVivasDelDonante(A<string>._)).Returns(Task.FromResult(new List<ProductoIncompletoDTO>()));
            A.CallTo(() => transaccion.LeerStock(A<string>._, A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["40252"] = 3, ["43415"] = 1 }));
            A.CallTo(() => transaccion.SiguienteTraspaso()).Returns(Task.FromResult(81500));
            A.CallTo(() => transaccion.Insertar(A<ProductoIncompletoDTO>._, A<Guid?>._)).Returns(Task.FromResult(12));
            A.CallTo(() => transaccion.LeerFicha(A<int>._)).ReturnsLazily((int id) => Task.FromResult(Ficha(id)));
            A.CallTo(() => impresora.Imprimir(A<ProductoIncompletoDTO>._, A<string>._, A<bool>._))
                .ReturnsLazily((ProductoIncompletoDTO f, string u, bool e) => new EtiquetaIncompletoDTO { Impresa = !e, Codigo = f.CodigoEtiqueta });
            A.CallTo(() => avisador.AvisarACompras(A<NotificacionPushDTO>._)).Returns(Task.FromResult(true));
        }

        private ServicioProductosIncompletos Servicio() => new ServicioProductosIncompletos(repositorio, avisador, impresora);

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static async Task<NestoBusinessException> Lanza(Func<Task> accion)
        {
            try
            {
                await accion();
            }
            catch (NestoBusinessException ex)
            {
                return ex;
            }
            Assert.Fail("Tenía que lanzar NestoBusinessException");
            return null;
        }

        [TestMethod]
        public async Task Sacar_EnAlgete_UnTraspasoConLaPiezaYLaUnidad_HuecoPropuesto_EtiquetaYAviso()
        {
            ResultadoProductoIncompletoDTO r = await Servicio().SacarPieza(Peticion(), false, Usuario("Santi", "Almacén"));

            Assert.AreEqual(81500, r.NumeroTraspaso);
            Assert.AreEqual(12, r.Ficha.Id);
            Assert.IsTrue(r.UnidadMovida);
            Assert.AreEqual("012/003/001", r.Hueco);
            Assert.IsTrue(r.HuecoPropuesto);
            Assert.AreEqual(1, r.UnidadesPendientesDeUbicar);
            Assert.AreEqual("_SacaPieza", r.Apuntes[0].Diario);
            A.CallTo(() => transaccion.Contabilizar("_SacaPieza", 81500, A<IReadOnlyList<LineaMovimientoTraspaso>>.That.Matches(l =>
                l.Count == 3 && l.Any(x => x.Almacen == "INC" && x.Producto == "40252" && x.Cantidad == 1)), A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.SacarDeUbicaciones("ALG", "40252", A<HuecoUbicacion>.That.Matches(h => h.Codigo == "012003001"), 1, 81500, A<string>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.DejarPendienteDeUbicar("ALG", "43415", 1, 81500, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.Insertar(A<ProductoIncompletoDTO>.That.Matches(f => f.NumeroSerie == "101270" && f.AlmacenIncompletos == "INC"
                && f.HuecoOrigen == "012/003/001" && f.Pedido == 931421), A<Guid?>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.ApuntarRastro("Sacar pieza · ficha 12", A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
            Assert.IsTrue(r.Etiqueta.Impresa);
            Assert.AreEqual("INC12", r.Etiqueta.Codigo);
            Assert.IsTrue(r.AvisoCompras);
        }

        [TestMethod]
        public async Task Sacar_Ensayo_NoEscribeNiImprimeNiAvisa()
        {
            ResultadoProductoIncompletoDTO r = await Servicio().SacarPieza(Peticion(), true, Usuario("Santi", "Almacén"));

            Assert.IsTrue(r.Ensayo);
            Assert.AreEqual(3, r.Apuntes.Count);
            StringAssert.StartsWith(r.Mensaje, "Se sacaría");
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionProductosIncompletos, Task<ResultadoProductoIncompletoDTO>>>._)).MustNotHaveHappened();
            A.CallTo(() => impresora.Imprimir(A<ProductoIncompletoDTO>._, A<string>._, true)).MustHaveHappenedOnceExactly();
            A.CallTo(() => impresora.Imprimir(A<ProductoIncompletoDTO>._, A<string>._, false)).MustNotHaveHappened();
            A.CallTo(() => avisador.AvisarACompras(A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Sacar_SinNumeroDeSerieDeUnProductoQueLoLleva_400()
        {
            NestoBusinessException ex = await Lanza(() => Servicio().SacarPieza(Peticion(serie: null), false, Usuario("Santi", "Almacén")));
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            StringAssert.Contains(ex.Message, "lleva número de serie");
        }

        [TestMethod]
        public async Task Sacar_SinTabla_503()
        {
            A.CallTo(() => repositorio.TablaCreada()).Returns(Task.FromResult(false));
            NestoBusinessException ex = await Lanza(() => Servicio().SacarPieza(Peticion(), true, Usuario("Santi", "Almacén")));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            StringAssert.Contains(ex.Message, "Issue902_ProductosIncompletos.sql");
        }

        [TestMethod]
        public async Task Sacar_SinAlmacenDeIncompletos_503()
        {
            A.CallTo(() => repositorio.LeerAlmacenIncompletos()).Returns(Task.FromResult<string>(null));
            NestoBusinessException ex = await Lanza(() => Servicio().SacarPieza(Peticion(), true, Usuario("Santi", "Almacén")));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        }

        [TestMethod]
        public async Task Sacar_ComprasOUnVendedor_403_YLaTiendaConOtroAlmacen_403()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Lanza(() => Servicio().SacarPieza(Peticion(), true, Usuario("Marta", "Compras")))).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Lanza(() => Servicio().SacarPieza(Peticion(), true, Usuario("Israel")))).StatusCode);
            A.CallTo(() => repositorio.LeerParametroPropio("Paloma", "AlmacénPedidoVta")).Returns(Task.FromResult("REI"));
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Lanza(() => Servicio().SacarPieza(Peticion("ALG"), true, Usuario("Paloma", "Tiendas")))).StatusCode);
        }

        [TestMethod]
        public async Task Sacar_LaTiendaConLoSuyo_SinHuecos()
        {
            A.CallTo(() => repositorio.LeerParametroPropio("Paloma", "AlmacénPedidoVta")).Returns(Task.FromResult("REI"));

            ResultadoProductoIncompletoDTO r = await Servicio().SacarPieza(Peticion("REI"), false, Usuario("Paloma", "Tiendas"));

            Assert.IsNull(r.Hueco);
            Assert.AreEqual(0, r.UnidadesPendientesDeUbicar);
            A.CallTo(() => transaccion.SacarDeUbicaciones(A<string>._, A<string>._, A<HuecoUbicacion>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => transaccion.DejarPendienteDeUbicar(A<string>._, A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => transaccion.Contabilizar("_SacaPieza", 81500, A<IReadOnlyList<LineaMovimientoTraspaso>>.That.Matches(l => l.All(x => x.Almacen != "ALG")), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Sacar_SinStockDelDonante_400()
        {
            A.CallTo(() => repositorio.LeerStock("ALG", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int>()));
            NestoBusinessException ex = await Lanza(() => Servicio().SacarPieza(Peticion(), false, Usuario("Santi", "Almacén")));
            StringAssert.Contains(ex.Message, "hay 0 de 40252");
        }

        [TestMethod]
        public async Task Sacar_OtraPiezaDeUnaUnidadYaIncompleta_NoMueveLaUnidad()
        {
            A.CallTo(() => repositorio.LeerFichas(true, null, "40252")).Returns(Task.FromResult(new List<ProductoIncompletoDTO> { Ficha(7) }));
            A.CallTo(() => transaccion.LeerVivasDelDonante("40252")).Returns(Task.FromResult(new List<ProductoIncompletoDTO> { Ficha(7) }));
            SacarPiezaDTO p = Peticion();
            p.Pieza = "43416";
            A.CallTo(() => repositorio.LeerProductos(A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, ProductoIncompletoLeido>(StringComparer.OrdinalIgnoreCase)
            {
                ["40252"] = new ProductoIncompletoLeido { Producto = "40252", Nombre = "DEPIBLEND", Ubicar = true, NecesitaNumSerie = true },
                ["43416"] = new ProductoIncompletoLeido { Producto = "43416", Nombre = "PEDAL", Ubicar = true }
            }));

            ResultadoProductoIncompletoDTO r = await Servicio().SacarPieza(p, false, Usuario("Santi", "Almacén"));

            Assert.IsFalse(r.UnidadMovida);
            A.CallTo(() => transaccion.Contabilizar("_SacaPieza", 81500, A<IReadOnlyList<LineaMovimientoTraspaso>>.That.Matches(l => l.Count == 1), A<string>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.SacarDeUbicaciones(A<string>._, A<string>._, A<HuecoUbicacion>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Sacar_ConLaMismaClave_DevuelveLaFichaSinRepetirNada()
        {
            Guid clave = Guid.NewGuid();
            A.CallTo(() => transaccion.LeerPorClave(clave)).Returns(Task.FromResult(Ficha(5)));
            SacarPiezaDTO p = Peticion();
            p.Clave = clave;

            ResultadoProductoIncompletoDTO r = await Servicio().SacarPieza(p, false, Usuario("Santi", "Almacén"));

            Assert.AreEqual(5, r.Ficha.Id);
            StringAssert.Contains(r.Mensaje, "ya estaba sacada");
            A.CallTo(() => transaccion.Contabilizar(A<string>._, A<int>._, A<IReadOnlyList<LineaMovimientoTraspaso>>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => impresora.Imprimir(A<ProductoIncompletoDTO>._, A<string>._, A<bool>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.AvisarACompras(A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Sacar_SiLaUnidadCambiaMientras_409()
        {
            A.CallTo(() => transaccion.LeerVivasDelDonante("40252")).Returns(Task.FromResult(new List<ProductoIncompletoDTO> { Ficha(7) }));
            NestoBusinessException ex = await Lanza(() => Servicio().SacarPieza(Peticion(), false, Usuario("Santi", "Almacén")));
            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            A.CallTo(() => transaccion.Contabilizar(A<string>._, A<int>._, A<IReadOnlyList<LineaMovimientoTraspaso>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Completar_LaUltimaPieza_LaUnidadVuelvePendienteDeUbicar()
        {
            A.CallTo(() => repositorio.LeerFicha(7)).Returns(Task.FromResult(Ficha(7)));
            A.CallTo(() => repositorio.LeerStock("INC", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["40252"] = 1 }));

            ResultadoProductoIncompletoDTO r = await Servicio().Completar(7, new CompletarProductoIncompletoDTO(), false, Usuario("Santi", "Almacén"));

            Assert.IsTrue(r.UnidadMovida);
            Assert.AreEqual(EstadosProductoIncompleto.COMPLETADO, r.Ficha.Estado);
            A.CallTo(() => transaccion.Contabilizar("_SacaPieza", 81500, A<IReadOnlyList<LineaMovimientoTraspaso>>.That.Matches(l => l.Count == 3), A<string>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.SacarDeUbicaciones("ALG", "43415", null, 1, 81500, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.DejarPendienteDeUbicar("ALG", "40252", 1, 81500, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.Cerrar(7, EstadosProductoIncompleto.COMPLETADO, A<string>._, 81500, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Completar_SiLeFaltaOtraPieza_SoloLaPieza()
        {
            A.CallTo(() => repositorio.LeerFicha(7)).Returns(Task.FromResult(Ficha(7)));
            A.CallTo(() => repositorio.LeerFichas(true, null, "40252")).Returns(Task.FromResult(new List<ProductoIncompletoDTO> { Ficha(7), Ficha(8) }));
            A.CallTo(() => transaccion.LeerVivasDelDonante("40252")).Returns(Task.FromResult(new List<ProductoIncompletoDTO> { Ficha(7), Ficha(8) }));

            ResultadoProductoIncompletoDTO r = await Servicio().Completar(7, null, false, Usuario("Santi", "Almacén"));

            Assert.IsFalse(r.UnidadMovida);
            StringAssert.Contains(r.Mensaje, "sigue incompleto");
            A.CallTo(() => transaccion.Contabilizar("_SacaPieza", 81500, A<IReadOnlyList<LineaMovimientoTraspaso>>.That.Matches(l => l.Count == 1), A<string>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.DejarPendienteDeUbicar(A<string>._, A<string>._, A<int>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Completar_SinLaPiezaEnElAlmacen_400_YCerrada_409_YNoExiste_404()
        {
            A.CallTo(() => repositorio.LeerFicha(7)).Returns(Task.FromResult(Ficha(7)));
            A.CallTo(() => repositorio.LeerStock("ALG", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int>()));
            NestoBusinessException sinPieza = await Lanza(() => Servicio().Completar(7, null, false, Usuario("Santi", "Almacén")));
            Assert.AreEqual(HttpStatusCode.BadRequest, sinPieza.StatusCode);
            StringAssert.Contains(sinPieza.Message, "recambio");

            A.CallTo(() => repositorio.LeerFicha(8)).Returns(Task.FromResult(Ficha(8, estado: EstadosProductoIncompleto.COMPLETADO)));
            Assert.AreEqual(HttpStatusCode.Conflict, (await Lanza(() => Servicio().Completar(8, null, false, Usuario("Santi", "Almacén")))).StatusCode);

            A.CallTo(() => repositorio.LeerFicha(9)).Returns(Task.FromResult<ProductoIncompletoDTO>(null));
            Assert.AreEqual(HttpStatusCode.NotFound, (await Lanza(() => Servicio().Completar(9, null, false, Usuario("Santi", "Almacén")))).StatusCode);
        }

        [TestMethod]
        public async Task Completar_ConHuecoDeLaPieza_SaleDeEseHueco()
        {
            A.CallTo(() => repositorio.LeerFicha(7)).Returns(Task.FromResult(Ficha(7)));
            A.CallTo(() => repositorio.LeerStock("INC", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["40252"] = 1 }));

            _ = await Servicio().Completar(7, new CompletarProductoIncompletoDTO { Pasillo = "020", Fila = "001", Columna = "001" }, false, Usuario("Santi", "Almacén"));

            A.CallTo(() => transaccion.SacarDeUbicaciones("ALG", "43415", A<HuecoUbicacion>.That.Matches(h => h.Codigo == "020001001"), 1, 81500, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Baja_CierraTodasLasFichasDeLaUnidad_YSacaLaUnidadDeIncompletos()
        {
            A.CallTo(() => repositorio.LeerFicha(7)).Returns(Task.FromResult(Ficha(7)));
            A.CallTo(() => repositorio.LeerStock("INC", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["40252"] = 1 }));
            A.CallTo(() => transaccion.LeerStock("INC", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["40252"] = 1 }));
            A.CallTo(() => transaccion.LeerVivasDelDonante("40252")).Returns(Task.FromResult(new List<ProductoIncompletoDTO> { Ficha(7), Ficha(8) }));

            ResultadoProductoIncompletoDTO r = await Servicio().DarDeBaja(7, new BajaProductoIncompletoDTO { Motivo = "Carcasa rota" }, false, Usuario("Alfredo", "Dirección"));

            CollectionAssert.AreEqual(new[] { 7, 8 }, r.FichasCerradas);
            A.CallTo(() => transaccion.Contabilizar("_BajaIncom", 81500, A<IReadOnlyList<LineaMovimientoTraspaso>>.That.Matches(l =>
                l.Count == 1 && l[0].Almacen == "INC" && l[0].Cantidad == -1), A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.Cerrar(A<int>._, EstadosProductoIncompleto.BAJA, "Carcasa rota", 81500, A<string>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task Baja_SinMotivo_400_YEnsayoNoEscribe()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Lanza(() => Servicio().DarDeBaja(7, new BajaProductoIncompletoDTO(), false, Usuario("Santi", "Almacén")))).StatusCode);

            A.CallTo(() => repositorio.LeerFicha(7)).Returns(Task.FromResult(Ficha(7)));
            A.CallTo(() => repositorio.LeerStock("INC", A<IEnumerable<string>>._)).Returns(Task.FromResult(new Dictionary<string, int> { ["40252"] = 1 }));
            ResultadoProductoIncompletoDTO r = await Servicio().DarDeBaja(7, new BajaProductoIncompletoDTO { Motivo = "Rota" }, true, Usuario("Santi", "Almacén"));
            Assert.IsTrue(r.Ensayo);
            A.CallTo(() => repositorio.EnTransaccion(A<Func<ITransaccionProductosIncompletos, Task<ResultadoProductoIncompletoDTO>>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Listar_LaTiendaSoloLoSuyo_YComprasTodo()
        {
            A.CallTo(() => repositorio.LeerParametroPropio("Paloma", "AlmacénPedidoVta")).Returns(Task.FromResult("REI"));
            _ = await Servicio().Listar(true, null, null, Usuario("Paloma", "Tiendas"));
            A.CallTo(() => repositorio.LeerFichas(true, "REI", null)).MustHaveHappenedOnceExactly();

            _ = await Servicio().Listar(false, null, "40252", Usuario("Marta", "Compras"));
            A.CallTo(() => repositorio.LeerFichas(false, null, "40252")).MustHaveHappenedOnceExactly();

            Assert.AreEqual(HttpStatusCode.Forbidden, (await Lanza(() => Servicio().Listar(true, null, null, Usuario("Israel")))).StatusCode);
        }

        [TestMethod]
        public async Task PiezasSacadas_SinProducto_400_YSinTabla_503()
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Lanza(() => Servicio().PiezasSacadas(" ", Usuario("Santi", "Almacén")))).StatusCode);
            A.CallTo(() => repositorio.TablaCreada()).Returns(Task.FromResult(false));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Lanza(() => Servicio().PiezasSacadas("40252", Usuario("Santi", "Almacén")))).StatusCode);
        }

        [TestMethod]
        public async Task Controlador_DevuelveLoQueDiceElServicio()
        {
            IServicioProductosIncompletos servicio = A.Fake<IServicioProductosIncompletos>();
            A.CallTo(() => servicio.SacarPieza(A<SacarPiezaDTO>._, true, A<IPrincipal>._))
                .Returns(Task.FromResult(new ResultadoProductoIncompletoDTO { Ensayo = true }));
            A.CallTo(() => servicio.Listar(true, null, null, A<IPrincipal>._)).Returns(Task.FromResult(new ListadoProductosIncompletosDTO
            {
                Fichas = new List<ProductoIncompletoDTO> { Ficha() }
            }));
            var controlador = new ProductosIncompletosController(null, servicio);

            var sacar = await controlador.PostSacarPieza(Peticion(), true) as OkNegotiatedContentResult<ResultadoProductoIncompletoDTO>;
            Assert.IsTrue(sacar.Content.Ensayo);
            var lista = await controlador.GetProductosIncompletos() as OkNegotiatedContentResult<ListadoProductosIncompletosDTO>;
            Assert.AreEqual(1, lista.Content.Total);
        }

        [TestMethod]
        public async Task Controlador_LosErroresDelServicioSuben()
        {
            IServicioProductosIncompletos servicio = A.Fake<IServicioProductosIncompletos>();
            A.CallTo(() => servicio.Completar(7, A<CompletarProductoIncompletoDTO>._, false, A<IPrincipal>._))
                .Throws(new NestoBusinessException(ReglasProductosIncompletos.MENSAJE_FALTA_SCRIPT) { StatusCode = HttpStatusCode.ServiceUnavailable });
            var controlador = new ProductosIncompletosController(null, servicio);
            NestoBusinessException ex = await Lanza(() => controlador.PostCompletar(7, null));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        }
    }
}
