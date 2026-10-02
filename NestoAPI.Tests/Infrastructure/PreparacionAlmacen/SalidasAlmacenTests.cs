using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: la salida de mercancía es el espejo de la recepción. Un núcleo común (recorrido, casar lo leído,
    /// faltas, terminar, permisos) y una estrategia por tipo: PICK (picking de pedidos) y REPO (salida de un traspaso
    /// de reposición desde el almacén de origen).
    /// </summary>
    [TestClass]
    public class SalidasAlmacenTests
    {
        private const string EMPRESA = "1";
        private IRepositorioPreparacionAlmacen repositorio;
        private ServicioSalidas servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            servicio = new ServicioSalidas(new IOrigenSalida[]
            {
                new OrigenSalidaPicking(repositorio),
                new OrigenSalidaReposicion(repositorio)
            });
        }

        private static IPrincipal Usuario(params string[] grupos)
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Andre") }, "prueba");
            return new GenericPrincipal(identidad, grupos);
        }

        private static LineaPickingAlmacenDTO Linea(string producto, int cantidad, string pasillo)
            => new LineaPickingAlmacenDTO { Producto = producto, Descripcion = "Producto " + producto, CodigoBarras = "84" + producto, Cantidad = cantidad, Pasillo = pasillo, Fila = "001", Columna = "001" };

        [TestMethod]
        public async Task LeerPendientes_JuntaLosPickingsYLasReposicionesPorSalir()
        {
            A.CallTo(() => repositorio.LeerPickingsEnCurso(EMPRESA, "ALG")).Returns(new List<PickingEnCursoDTO>
            {
                new PickingEnCursoDTO { Picking = 99700, Lineas = 26, Pedidos = 7, Unidades = 80 }
            });
            A.CallTo(() => repositorio.LeerReposicionesPorSalir(EMPRESA, "ALG")).Returns(new List<ReposicionPorSalir>
            {
                new ReposicionPorSalir { Traspaso = 80872, Destino = "REI", Lineas = 31, Unidades = 95 }
            });

            List<RecogidaPendienteDTO> pendientes = await servicio.LeerPendientes(EMPRESA, "ALG");

            Assert.AreEqual(2, pendientes.Count);
            RecogidaPendienteDTO repo = pendientes.Single(p => p.Tipo == "REPO");
            Assert.AreEqual(80872, repo.Numero);
            Assert.AreEqual("REI", repo.Destino);
            Assert.AreEqual(31, repo.Lineas);
            Assert.AreEqual(95, repo.Unidades);
            Assert.IsNull(repo.Pedidos);
            Assert.AreEqual("Mesa de packing", pendientes.Single(p => p.Tipo == "PICK").Destino);
        }

        [TestMethod]
        public async Task LeerRecogida_UnaReposicion_TraeSuRecorridoConLoLeidoYSuDestino()
        {
            A.CallTo(() => repositorio.LeerReposicionSalida(EMPRESA, 80872)).Returns(new ReposicionSalida
            {
                Destino = "REI",
                Lineas = new List<LineaPickingAlmacenDTO> { Linea("B", 2, "005"), Linea("A", 3, "001") }
            });
            A.CallTo(() => repositorio.LeerLecturasDeSalida(EMPRESA, "REPO", 80872)).Returns(new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A", Unidades = 3 }
            });

            RecogidaAlmacenDTO recogida = await servicio.LeerRecogida(EMPRESA, " repo ", 80872);

            Assert.AreEqual("REPO", recogida.Tipo);
            Assert.AreEqual("REI", recogida.Destino);
            Assert.AreEqual("A", recogida.Lineas[0].Producto, "En el orden de la estantería");
            Assert.AreEqual(3, recogida.Lineas[0].Resuelto);
            Assert.AreEqual(2, recogida.SiguienteOrden);
        }

        [TestMethod]
        public async Task LeerRecogida_TipoQueNoExisteOSinNadaQueRecoger_Null()
        {
            A.CallTo(() => repositorio.LeerReposicionSalida(EMPRESA, 1)).Returns(Task.FromResult<ReposicionSalida>(null));
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 1)).Returns(new List<LineaPickingAlmacenDTO>());

            Assert.IsNull(await servicio.LeerRecogida(EMPRESA, "OTRO", 1));
            Assert.IsNull(await servicio.LeerRecogida(EMPRESA, "REPO", 1));
            Assert.IsNull(await servicio.LeerRecogida(EMPRESA, "PICK", 1));
        }

        private void PickingConLecturas(int picking, params LecturaPickingAlmacen[] lecturas)
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, picking)).Returns(new List<LineaPickingAlmacenDTO>
            {
                Linea("A", 3, "001"), Linea("B", 1, "002")
            });
            A.CallTo(() => repositorio.LeerLecturasDelPicking(EMPRESA, picking)).Returns(lecturas.ToList());
        }

        [TestMethod]
        public async Task Terminar_UnPickingConParadasSinResolver_NoSeTerminaYDiceCuantoFalta()
        {
            PickingConLecturas(99700, new LecturaPickingAlmacen { Producto = "A", Unidades = 3 });

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.SinTerminar, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "1");
        }

        [TestMethod]
        public async Task Terminar_UnPickingCompleto_QuedaTerminadoYCompleto()
        {
            PickingConLecturas(99700,
                new LecturaPickingAlmacen { Producto = "A", Unidades = 3 },
                new LecturaPickingAlmacen { Producto = "B", Unidades = 1 });

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.IsTrue(resultado.Salida.Completa);
            Assert.AreEqual(0, resultado.Salida.UnidadesEnFalta);
        }

        [TestMethod]
        public async Task Terminar_UnPickingConFaltas_SeTerminaYDiceQueHayQueQuitarlasDelPedido()
        {
            PickingConLecturas(99700,
                new LecturaPickingAlmacen { Producto = "A", Unidades = 2, Faltas = 1 },
                new LecturaPickingAlmacen { Producto = "B", Faltas = 1 });

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.IsFalse(resultado.Salida.Completa);
            Assert.AreEqual(2, resultado.Salida.UnidadesEnFalta);
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, resultado.Salida.Productos.Where(p => p.Faltas > 0).Select(p => p.Producto).ToList());
            StringAssert.Contains(resultado.Salida.Mensaje, "falta");
        }

        [TestMethod]
        public async Task Terminar_SinPermisoDeAlmacen_NoSeTermina()
        {
            PickingConLecturas(99700,
                new LecturaPickingAlmacen { Producto = "A", Unidades = 3 },
                new LecturaPickingAlmacen { Producto = "B", Unidades = 1 });

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Tiendas"));

            Assert.AreEqual(EstadoTerminarSalida.SinPermiso, resultado.Estado);
        }

        [TestMethod]
        public async Task Terminar_DireccionTambienPuede()
        {
            PickingConLecturas(99700,
                new LecturaPickingAlmacen { Producto = "A", Unidades = 3 },
                new LecturaPickingAlmacen { Producto = "B", Unidades = 1 });

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("Dirección"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
        }

        [TestMethod]
        public async Task Terminar_UnaReposicion_TodaviaSeCierraEnNesto()
        {
            A.CallTo(() => repositorio.LeerReposicionSalida(EMPRESA, 80872)).Returns(new ReposicionSalida
            {
                Destino = "REI",
                Lineas = new List<LineaPickingAlmacenDTO> { Linea("A", 3, "001") }
            });
            A.CallTo(() => repositorio.LeerLecturasDeSalida(EMPRESA, "REPO", 80872)).Returns(new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A", Unidades = 3 }
            });

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "REPO", 80872, Usuario("Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.NoSeTerminaAqui, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "Nesto");
        }

        [TestMethod]
        public async Task Terminar_TipoQueNoExisteONadaQueRecoger()
        {
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 5)).Returns(new List<LineaPickingAlmacenDTO>());

            Assert.AreEqual(EstadoTerminarSalida.TipoNoValido, (await servicio.Terminar(EMPRESA, "OTRO", 5, Usuario("Almacén"))).Estado);
            Assert.AreEqual(EstadoTerminarSalida.NoExiste, (await servicio.Terminar(EMPRESA, "PICK", 5, Usuario("Almacén"))).Estado);
        }
    }
}
