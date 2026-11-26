using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
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

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// Sugerencia 1429 (Manuel, 26/11/26): devolver a Algete el exceso de una tienda. LA REGLA (exceso = min(stock − máximo,
    /// stock − pendiente); sin máximo = máximo 0, marcado), el filtro obligatorio, el tope de 500 y el endpoint.
    /// </summary>
    [TestClass]
    public class ExcesoReposicionTests
    {
        private static FilaExcesoReposicion Fila(string producto, int stock, int pendiente, int? maximo, string nombre = null, string familia = "FAB")
            => new FilaExcesoReposicion
            {
                Producto = producto, Nombre = nombre ?? "Producto " + producto, Familia = familia, NombreFamilia = familia,
                Stock = stock, Pendiente = pendiente, StockMaximo = maximo
            };

        private static PeticionExcesoReposicionDTO Peticion(string familia = "Faby ") => new PeticionExcesoReposicionDTO
        {
            Empresa = "1", Origen = "alc", Familia = familia
        };

        // ---------------------------------------------------------------- la regla

        [TestMethod]
        public void Exceso_LoQuePasaDelMaximo()
        {
            Assert.AreEqual(5, ReglaExcesoReposicion.Exceso(stock: 7, pendiente: 0, maximo: 2));
        }

        [TestMethod]
        public void Exceso_ElPendienteLoTopa()
        {
            // 7 en la tienda, máximo 2, pero 4 pendientes de servir allí: solo se pueden mandar 3
            Assert.AreEqual(3, ReglaExcesoReposicion.Exceso(stock: 7, pendiente: 4, maximo: 2));
            // con un pendiente menor que el máximo manda el máximo
            Assert.AreEqual(5, ReglaExcesoReposicion.Exceso(stock: 7, pendiente: 1, maximo: 2));
        }

        [TestMethod]
        public void Exceso_MaximoCero_TodoLoDisponible()
        {
            Assert.AreEqual(6, ReglaExcesoReposicion.Exceso(stock: 7, pendiente: 1, maximo: 0));
        }

        [TestMethod]
        public void Exceso_NadaDeExceso_Cero()
        {
            Assert.AreEqual(0, ReglaExcesoReposicion.Exceso(stock: 2, pendiente: 0, maximo: 2));
            Assert.AreEqual(0, ReglaExcesoReposicion.Exceso(stock: 2, pendiente: 0, maximo: 5));
            Assert.AreEqual(0, ReglaExcesoReposicion.Exceso(stock: 3, pendiente: 5, maximo: 0));
        }

        [TestMethod]
        public void Calcular_SinMaximo_EntraEnteroYMarcado()
        {
            List<LineaExcesoReposicionDTO> lineas = ReglaExcesoReposicion.Calcular(new[] { Fila("12345", stock: 3, pendiente: 1, maximo: null) });

            Assert.AreEqual(1, lineas.Count);
            Assert.AreEqual(2, lineas[0].Exceso);
            Assert.IsTrue(lineas[0].SinMaximo);
            Assert.AreEqual(0, lineas[0].StockMaximo);
        }

        [TestMethod]
        public void Calcular_ConMaximoCero_NoVaMarcadoSinMaximo()
        {
            LineaExcesoReposicionDTO linea = ReglaExcesoReposicion.Calcular(new[] { Fila("1", 3, 0, 0) }).Single();
            Assert.AreEqual(3, linea.Exceso);
            Assert.IsFalse(linea.SinMaximo);
        }

        [TestMethod]
        public void Calcular_SoloLasDeExcesoYOrdenadasPorFamiliaYNombre()
        {
            List<LineaExcesoReposicionDTO> lineas = ReglaExcesoReposicion.Calcular(new[]
            {
                Fila("1", 5, 0, 2, "Zeta", "FAB"),
                Fila("2", 2, 0, 2, "Sin exceso", "FAB"),
                Fila("3", 4, 0, 1, "Alfa", "FAB"),
                Fila("4", 4, 0, 1, "Beta", "CER")
            });

            CollectionAssert.AreEqual(new[] { "4", "3", "1" }, lineas.Select(l => l.Producto).ToArray());
        }

        [TestMethod]
        public void Calcular_MasDe500Lineas_400ConCuantas()
        {
            IEnumerable<FilaExcesoReposicion> filas = Enumerable.Range(1, 501).Select(i => Fila(i.ToString(), 3, 0, 1));

            var ex = Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Calcular(filas));
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            StringAssert.Contains(ex.Message, "501");
            StringAssert.Contains(ex.Message, "acote");
        }

        [TestMethod]
        public void Calcular_500Lineas_Valen()
        {
            Assert.AreEqual(500, ReglaExcesoReposicion.Calcular(Enumerable.Range(1, 500).Select(i => Fila(i.ToString(), 3, 0, 1))).Count);
        }

        [TestMethod]
        public void Normalizar_SinNingunFiltro_400()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO
            {
                Origen = "ALC", Familia = "  ", Productos = new List<string> { " " }
            }));
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [TestMethod]
        public void Normalizar_DestinoPorDefectoAlgeteYFiltroEnMayusculas()
        {
            FiltroExcesoReposicion filtro = ReglaExcesoReposicion.Normalizar(Peticion(" faby  "));
            Assert.AreEqual("ALC", filtro.Origen);
            Assert.AreEqual("ALG", filtro.Destino);
            Assert.AreEqual("FABY", filtro.Familia);
        }

        [TestMethod]
        public void Normalizar_OrigenQueNoEsTiendaOMismoDestino_400()
        {
            Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALG", Familia = "X" }));
            Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "REI", Destino = "REI", Familia = "X" }));
        }

        [TestMethod]
        public void Resultado_TotalesYMensajeConLasSinMaximo()
        {
            FiltroExcesoReposicion filtro = ReglaExcesoReposicion.Normalizar(Peticion());
            ResultadoExcesoReposicionDTO r = ReglaExcesoReposicion.Resultado(filtro, new[] { Fila("1", 7, 1, 2), Fila("2", 3, 0, null) }, ensayo: true);

            Assert.AreEqual(2, r.NumeroLineas);
            Assert.AreEqual(8, r.TotalUnidades);
            Assert.AreEqual(1, r.LineasSinMaximo);
            Assert.AreEqual("Se mandarían 8 unidades de 2 referencias de Alcobendas a Algete. De ellas, 1 sin máximo puesto en la tienda, que se devuelven enteras.",
                r.Mensaje);
        }

        [TestMethod]
        public void Resultado_SinExceso_MensajeNoHayExceso()
        {
            ResultadoExcesoReposicionDTO r = ReglaExcesoReposicion.Resultado(ReglaExcesoReposicion.Normalizar(Peticion()), new[] { Fila("1", 2, 0, 2) }, true);
            Assert.AreEqual(0, r.NumeroLineas);
            StringAssert.StartsWith(r.Mensaje, "No hay exceso");
        }

        // ---------------------------------------------------------------- el endpoint

        private IServicioPreparacionReposicion preparacion;
        private IPermisoRellenarReposicionManual permiso;
        private ILectorExcesoReposicion lector;

        [TestInitialize]
        public void Preparar()
        {
            preparacion = A.Fake<IServicioPreparacionReposicion>();
            permiso = A.Fake<IPermisoRellenarReposicionManual>();
            lector = A.Fake<ILectorExcesoReposicion>();
            A.CallTo(() => permiso.Puede(A<IPrincipal>._)).Returns(true);
            A.CallTo(() => preparacion.PuedeEscribir(A<IPrincipal>._, A<string>._, A<string>._, A<bool>._)).Returns(true);
            A.CallTo(() => lector.Leer(A<FiltroExcesoReposicion>._)).Returns(new List<FilaExcesoReposicion>
            {
                Fila("12345", 7, 1, 2), Fila("67890", 3, 0, null)
            });
        }

        private ReposicionesController Controlador(params Claim[] claims)
        {
            var lista = new List<Claim> { new Claim(ClaimTypes.Name, "Manuel") };
            lista.AddRange(claims);
            return new ReposicionesController(null, preparacion, permisoManual: permiso)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(lista, "Bearer")),
                LectorExceso = lector
            };
        }

        private static HttpStatusCode Estado(IHttpActionResult resultado)
            => ((ResponseMessageResult)resultado).Response.StatusCode;

        [TestMethod]
        public async Task PostExceso_SinFiltro_400SinLeerNada()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Controlador().PostExceso(new PeticionExcesoReposicionDTO { Origen = "ALC" }));
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            A.CallTo(() => lector.Leer(A<FiltroExcesoReposicion>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostExceso_SinPermisoManual_403()
        {
            A.CallTo(() => permiso.Puede(A<IPrincipal>._)).Returns(false);
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await Controlador().PostExceso(Peticion())));
            A.CallTo(() => lector.Leer(A<FiltroExcesoReposicion>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostExceso_SinPermisoEnLaTienda_403()
        {
            A.CallTo(() => preparacion.PuedeEscribir(A<IPrincipal>._, "1", "ALC", A<bool>._)).Returns(false);
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await Controlador().PostExceso(Peticion())));
        }

        [TestMethod]
        public async Task PostExceso_ClienteDeLaTienda_403()
        {
            Assert.AreEqual(HttpStatusCode.Forbidden, Estado(await Controlador(new Claim("cliente", "15191")).PostExceso(Peticion())));
            A.CallTo(() => lector.Leer(A<FiltroExcesoReposicion>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostExceso_Ensayo_CalculaYNoCrea()
        {
            var ok = (OkNegotiatedContentResult<ResultadoExcesoReposicionDTO>)await Controlador().PostExceso(Peticion());

            Assert.IsTrue(ok.Content.Ensayo);
            Assert.AreEqual(2, ok.Content.NumeroLineas);
            Assert.AreEqual(8, ok.Content.TotalUnidades);
            Assert.IsNull(ok.Content.Reposicion);
            A.CallTo(() => preparacion.Crear(A<CrearReposicionDTO>._, A<IPrincipal>._)).MustNotHaveHappened();
            A.CallTo(() => lector.Leer(A<FiltroExcesoReposicion>.That.Matches(f => f.Origen == "ALC" && f.Destino == "ALG" && f.Familia == "FABY")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostExceso_DeVerdad_CreaPorElCaminoDeSiempreConLasLineasCalculadas()
        {
            CrearReposicionDTO pedida = null;
            var creada = new ReposicionEnPreparacionDTO { Empresa = "1", Origen = "ALC", Destino = "ALG" };
            A.CallTo(() => preparacion.Crear(A<CrearReposicionDTO>._, A<IPrincipal>._))
                .Invokes((CrearReposicionDTO p, IPrincipal u) => pedida = p)
                .Returns(creada);

            var ok = (OkNegotiatedContentResult<ResultadoExcesoReposicionDTO>)await Controlador().PostExceso(Peticion(), ensayo: false);

            Assert.IsFalse(ok.Content.Ensayo);
            Assert.AreSame(creada, ok.Content.Reposicion);
            Assert.AreEqual("ALC", pedida.Origen);
            Assert.AreEqual("ALG", pedida.Destino);
            CollectionAssert.AreEquivalent(new[] { "12345:5", "67890:3" }, pedida.Lineas.Select(l => l.Producto + ":" + l.Cantidad).ToArray());
            StringAssert.StartsWith(ok.Content.Mensaje, "Se ha creado la reposición");
        }

        [TestMethod]
        public async Task PostExceso_DeVerdadSinExceso_NoCreaNada()
        {
            A.CallTo(() => lector.Leer(A<FiltroExcesoReposicion>._)).Returns(new List<FilaExcesoReposicion> { Fila("1", 2, 0, 2) });

            var ok = (OkNegotiatedContentResult<ResultadoExcesoReposicionDTO>)await Controlador().PostExceso(Peticion(), ensayo: false);

            Assert.AreEqual(0, ok.Content.Lineas.Count);
            A.CallTo(() => preparacion.Crear(A<CrearReposicionDTO>._, A<IPrincipal>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostExceso_DeVerdad_ElConflictoDeLaCreacionSaleTalCual()
        {
            A.CallTo(() => preparacion.Crear(A<CrearReposicionDTO>._, A<IPrincipal>._))
                .Throws(new ReposicionYaEnPreparacionException("Ya hay una reposición en preparación"));

            var ex = await Assert.ThrowsExceptionAsync<ReposicionYaEnPreparacionException>(() => Controlador().PostExceso(Peticion(), ensayo: false));
            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
        }

        [TestMethod]
        public void Sql_ConListaDeProductos_UnParametroPorProducto()
        {
            string sql = LectorExcesoReposicionSql.Sql(2);
            StringAssert.Contains(sql, "IN (@p0, @p1)");
            Assert.IsFalse(LectorExcesoReposicionSql.Sql(0).Contains("@p0"));
            StringAssert.Contains(sql, "WITH (NOLOCK)");
        }

        // ---------------------------------------------------------------- filtros flexibles (sugerencia 1429, Manuel 26/11/26)

        private static FilaExcesoReposicion FilaConVenta(string producto, string nombre, int stock, DateTime? ultimaVenta)
        {
            FilaExcesoReposicion fila = Fila(producto, stock, 0, 0, nombre);
            fila.UltimaVenta = ultimaVenta;
            return fila;
        }

        private static readonly DateTime HOY = new DateTime(2026, 11, 26);

        [TestMethod]
        public void Normalizar_SoloTextoDelNombre_ValeComoFiltroDeSeleccion()
        {
            FiltroExcesoReposicion filtro = ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO
            {
                Origen = "ALC", TextoNombre = " esmalte   faby ", TextoNombreExcluir = "permanente"
            });
            CollectionAssert.AreEqual(new[] { "ESMALTE", "FABY" }, filtro.PalabrasNombre);
            CollectionAssert.AreEqual(new[] { "PERMANENTE" }, filtro.PalabrasExcluir);
        }

        [TestMethod]
        public void Normalizar_LosFiltrosQueNoSeleccionanNoBastan()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO
            {
                Origen = "ALC", TextoNombreExcluir = "PERMANENTE", StockMinimo = 3, SinVentasEnMeses = 12, DejarEnTienda = 2
            }));
            StringAssert.Contains(ex.Message, "texto del nombre");
        }

        [TestMethod]
        public void Normalizar_ValoresFueraDeRango_400()
        {
            Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALC", Familia = "X", SinVentasEnMeses = 0 }));
            Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALC", Familia = "X", SinVentasEnMeses = 61 }));
            Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALC", Familia = "X", StockMinimo = -1 }));
            Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALC", Familia = "X", DejarEnTienda = -1 }));
            Assert.ThrowsException<NestoBusinessException>(() => ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALC", TextoNombre = "a b c d e f g h i j k" }));
            Assert.IsNotNull(ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALC", Familia = "X", SinVentasEnMeses = 60, DejarEnTienda = 0, StockMinimo = 0 }));
        }

        [TestMethod]
        public void Calcular_TextoDelNombre_TodasLasPalabrasSinMayusculasYExcluida_Ninguna()
        {
            FiltroExcesoReposicion filtro = ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO
            {
                Origen = "ALC", TextoNombre = "esmalte faby", TextoNombreExcluir = "permanente"
            });
            List<LineaExcesoReposicionDTO> lineas = ReglaExcesoReposicion.Calcular(new[]
            {
                Fila("1", 3, 0, 0, "Faby esmalte rojo"),
                Fila("2", 3, 0, 0, "ESMALTE FABY PERMANENTE ROJO"),
                Fila("3", 3, 0, 0, "Esmalte tradicional"),
                Fila("4", 3, 0, 0, null)
            }, filtro, HOY);

            CollectionAssert.AreEqual(new[] { "1" }, lineas.Select(l => l.Producto).ToArray());
        }

        [TestMethod]
        public void Calcular_StockMinimo_SoloLasQueLoSuperan()
        {
            FiltroExcesoReposicion filtro = ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALC", Familia = "FAB", StockMinimo = 3 });
            List<LineaExcesoReposicionDTO> lineas = ReglaExcesoReposicion.Calcular(new[] { Fila("1", 3, 0, 0), Fila("2", 4, 0, 0) }, filtro, HOY);
            CollectionAssert.AreEqual(new[] { "2" }, lineas.Select(l => l.Producto).ToArray());
        }

        [TestMethod]
        public void Calcular_SinVentasEnMeses_FueraLasVendidasDentroDelPlazoYConLaFecha()
        {
            FiltroExcesoReposicion filtro = ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALC", Familia = "FAB", SinVentasEnMeses = 12 });
            List<LineaExcesoReposicionDTO> lineas = ReglaExcesoReposicion.Calcular(new[]
            {
                FilaConVenta("1", "A", 3, null),                          // nunca vendida: entra
                FilaConVenta("2", "B", 3, new DateTime(2025, 11, 25)),    // hace más de 12 meses: entra
                FilaConVenta("3", "C", 3, new DateTime(2025, 11, 26)),    // justo 12 meses: cuenta como vendida
                FilaConVenta("4", "D", 3, new DateTime(2026, 10, 1))      // vendida hace poco: fuera
            }, filtro, HOY);

            CollectionAssert.AreEqual(new[] { "1", "2" }, lineas.Select(l => l.Producto).ToArray());
            Assert.IsNull(lineas[0].UltimaVenta);
            Assert.AreEqual(new DateTime(2025, 11, 25), lineas[1].UltimaVenta);
        }

        [TestMethod]
        public void Calcular_SinPedirVentas_NoLlevaUltimaVenta()
        {
            List<LineaExcesoReposicionDTO> lineas = ReglaExcesoReposicion.Calcular(new[] { FilaConVenta("1", "A", 3, new DateTime(2026, 10, 1)) },
                ReglaExcesoReposicion.Normalizar(Peticion()), HOY);
            Assert.IsNull(lineas.Single().UltimaVenta);
        }

        [TestMethod]
        public void Calcular_DejarEnTienda_SustituyeAlMaximoConElTopeDelPendiente()
        {
            FiltroExcesoReposicion filtro = ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO { Origen = "ALC", Familia = "FAB", DejarEnTienda = 2 });
            List<LineaExcesoReposicionDTO> lineas = ReglaExcesoReposicion.Calcular(new[]
            {
                Fila("1", 7, 0, 5),      // la ficha dejaría 5; se dejan 2 → 5
                Fila("2", 7, 4, null),   // pendiente 4 topa → 3
                Fila("3", 2, 0, 0)       // se queda en la tienda entero
            }, filtro, HOY);

            CollectionAssert.AreEqual(new[] { "1:5", "2:3" }, lineas.Select(l => l.Producto + ":" + l.Exceso).ToArray());
        }

        [TestMethod]
        public void Resultado_DejarEnTienda_NoCuentaLasSinMaximoYFiltrosEnUnaLinea()
        {
            FiltroExcesoReposicion filtro = ReglaExcesoReposicion.Normalizar(new PeticionExcesoReposicionDTO
            {
                Origen = "ALC", Familia = "faby", TextoNombre = "esmalte", TextoNombreExcluir = "permanente gel", StockMinimo = 3,
                SinVentasEnMeses = 12, DejarEnTienda = 2
            });
            ResultadoExcesoReposicionDTO r = ReglaExcesoReposicion.Resultado(filtro, new[] { Fila("1", 7, 0, null, "Esmalte rojo") }, true, HOY);

            Assert.AreEqual(0, r.LineasSinMaximo);
            Assert.AreEqual("Filtros: familia FABY; el nombre lleva «ESMALTE»; el nombre no lleva «PERMANENTE» ni «GEL»; más de 3 unidades en la tienda; " +
                "sin ventas en Alcobendas en los últimos 12 meses; se dejan 2 unidades de cada referencia en la tienda.", r.Filtros);
            Assert.AreEqual("Se mandarían 5 unidades de 1 referencia de Alcobendas a Algete.", r.Mensaje);
        }

        [TestMethod]
        public void Resultado_SinFiltrosNuevos_DiceQueSeDejaElMaximoDeLaFicha()
        {
            ResultadoExcesoReposicionDTO r = ReglaExcesoReposicion.Resultado(ReglaExcesoReposicion.Normalizar(Peticion()), new[] { Fila("1", 7, 0, 2) }, true, HOY);
            Assert.AreEqual("Filtros: familia FABY; se deja en la tienda el máximo de la ficha.", r.Filtros);
        }

        [TestMethod]
        public void Sql_ConPalabrasYVentas_ParametrosYUltimaVentaSoloSiSePide()
        {
            string sql = LectorExcesoReposicionSql.Sql(0, 2, 1, true);
            StringAssert.Contains(sql, "UPPER(p.Nombre) LIKE @n0 ESCAPE '!'");
            StringAssert.Contains(sql, "UPPER(p.Nombre) LIKE @n1 ESCAPE '!'");
            StringAssert.Contains(sql, "NOT LIKE @x0 ESCAPE '!'");
            StringAssert.Contains(sql, "LEFT JOIN ventas v");
            StringAssert.Contains(sql, "l.Estado >= 2 AND l.Cantidad > 0");
            StringAssert.Contains(sql, "> @stockMinimo");
            Assert.IsFalse(LectorExcesoReposicionSql.Sql(0, 1, 0, false).Contains("ventas AS"));
            Assert.IsFalse(sql.Contains("STRING_SPLIT") || sql.Contains("TRY_CONVERT"));
        }

        [TestMethod]
        public void Like_EscapaLosComodines()
        {
            Assert.AreEqual("%50!%!_A![B!!%", LectorExcesoReposicionSql.Like("50%_A[B!"));
        }

        [TestMethod]
        public async Task PostExceso_ConFiltrosNuevos_LlegaAlLectorYVuelvenEnLaRespuesta()
        {
            var ok = (OkNegotiatedContentResult<ResultadoExcesoReposicionDTO>)await Controlador().PostExceso(new PeticionExcesoReposicionDTO
            {
                Empresa = "1", Origen = "ALC", TextoNombre = "producto", StockMinimo = 2, SinVentasEnMeses = 6, DejarEnTienda = 1
            });

            A.CallTo(() => lector.Leer(A<FiltroExcesoReposicion>.That.Matches(f => f.PalabrasNombre.Single() == "PRODUCTO"
                && f.StockMinimo == 2 && f.SinVentasEnMeses == 6 && f.DejarEnTienda == 1))).MustHaveHappenedOnceExactly();
            // 12345: stock 7, pendiente 1, se deja 1 → 6; 67890: stock 3 → 2
            CollectionAssert.AreEquivalent(new[] { "12345:6", "67890:2" }, ok.Content.Lineas.Select(l => l.Producto + ":" + l.Exceso).ToArray());
            StringAssert.StartsWith(ok.Content.Filtros, "Filtros: el nombre lleva «PRODUCTO»");
        }
    }
}
