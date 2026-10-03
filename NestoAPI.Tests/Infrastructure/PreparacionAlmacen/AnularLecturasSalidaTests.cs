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
    /// Ariadna#6: descartar lo que un mozo ha leído en una recogida (una prueba olvidada en la cola de la PDA). Nunca
    /// lecturas sueltas: «Deshacer» es la misma lectura en negativo y podría quedar una falta sin su deshacer. Se anula
    /// TODO lo de ese mozo en esa recogida, solo Dirección o Admin, y nunca si la recogida ya se terminó (lo que faltaba
    /// ya se quitó del pedido).
    /// </summary>
    [TestClass]
    public class AnularLecturasSalidaTests
    {
        private const string EMPRESA = "1";
        private IRepositorioPreparacionAlmacen repositorio;
        private RepositorioSalidasFalso escrituras;
        private IRepositorioAnulacionLecturas anulaciones;
        private ServicioSalidas servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            escrituras = new RepositorioSalidasFalso();
            anulaciones = A.Fake<IRepositorioAnulacionLecturas>();
            servicio = new ServicioSalidas(new IOrigenSalida[]
            {
                new OrigenSalidaPicking(repositorio),
                new OrigenSalidaReposicion(repositorio)
            }, escrituras, anulaciones);
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var identidad = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, nombre) }, "prueba");
            return new GenericPrincipal(identidad, grupos);
        }

        [TestMethod]
        public async Task Anular_Direccion_AnulaTodoLoDeEseMozoEnEsaRecogida()
        {
            A.CallTo(() => anulaciones.Anular(EMPRESA, "PICK", 99739, "Pedro", "Carlos")).Returns(4);

            ResultadoAnularLecturas resultado = await servicio.AnularLecturas(EMPRESA, "pick", 99739, " Pedro ", Usuario("Carlos", "Dirección"));

            Assert.AreEqual(EstadoAnularLecturas.Anuladas, resultado.Estado);
            Assert.AreEqual(4, resultado.Filas);
            StringAssert.Contains(resultado.Mensaje, "Pedro");
            A.CallTo(() => anulaciones.Anular(EMPRESA, "PICK", 99739, "Pedro", "Carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Anular_Admin_Puede()
        {
            A.CallTo(() => anulaciones.Anular(A<string>._, A<string>._, A<int>._, A<string>._, A<string>._)).Returns(0);

            ResultadoAnularLecturas resultado = await servicio.AnularLecturas(EMPRESA, "REPO", 80872, "Andre", Usuario("Carlos", "Admin"));

            Assert.AreEqual(EstadoAnularLecturas.Anuladas, resultado.Estado);
        }

        [TestMethod]
        public async Task Anular_UnMozoDeAlmacen_NoPuedeYNoSeTocaNada()
        {
            ResultadoAnularLecturas resultado = await servicio.AnularLecturas(EMPRESA, "PICK", 99739, "Pedro", Usuario("Santiago", "Almacén"));

            Assert.AreEqual(EstadoAnularLecturas.SinPermiso, resultado.Estado);
            A.CallTo(() => anulaciones.Anular(A<string>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_RecogidaYaTerminada_NoSeAnulaYLoDice()
        {
            A.CallTo(() => anulaciones.Anular(EMPRESA, "PICK", 99739, "Pedro", "Carlos")).Returns((int?)null);

            ResultadoAnularLecturas resultado = await servicio.AnularLecturas(EMPRESA, "PICK", 99739, "Pedro", Usuario("Carlos", "Dirección"));

            Assert.AreEqual(EstadoAnularLecturas.YaTerminada, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "terminad");
        }

        [TestMethod]
        public async Task Anular_TipoQueNoEsUnaSalida_NoValido()
        {
            ResultadoAnularLecturas resultado = await servicio.AnularLecturas(EMPRESA, "COMP", 1234, "Pedro", Usuario("Carlos", "Dirección"));

            Assert.AreEqual(EstadoAnularLecturas.NoValido, resultado.Estado);
            A.CallTo(() => anulaciones.Anular(A<string>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Anular_SinDecirDeQueMozo_NoValido()
        {
            ResultadoAnularLecturas resultado = await servicio.AnularLecturas(EMPRESA, "PICK", 99739, "  ", Usuario("Carlos", "Dirección"));

            Assert.AreEqual(EstadoAnularLecturas.NoValido, resultado.Estado);
            A.CallTo(() => anulaciones.Anular(A<string>._, A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_DejaApuntadaLaSalidaComoTerminada()
        {
            // Ariadna#6: así se sabe después que ya no se puede anular lo leído
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 99700)).Returns(new List<LineaPickingAlmacenDTO>
            {
                new LineaPickingAlmacenDTO { Producto = "A", Descripcion = "A", CodigoBarras = "84A", Cantidad = 1, Pasillo = "001", Fila = "001", Columna = "001" }
            });
            A.CallTo(() => repositorio.LeerLecturasDelPicking(EMPRESA, 99700)).Returns(new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A", Unidades = 1 }
            });

            ResultadoTerminarSalida resultado = await servicio.Terminar(EMPRESA, "PICK", 99700, Usuario("NUEVAVISION\\Andre", "Almacén"));

            Assert.AreEqual(EstadoTerminarSalida.Terminada, resultado.Estado);
            Assert.AreEqual(("PICK", 99700, "NUEVAVISION\\Andre"), escrituras.Transaccion.Terminadas.Single());
        }
    }
}
