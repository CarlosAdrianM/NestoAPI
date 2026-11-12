using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#642 (corte 4 de #639): GET/PUT api/Almacen/Recorrido, el servicio y las reglas del mantenimiento.</summary>
    [TestClass]
    public class MantenimientoRecorridoTests
    {
        private static HuecoRecorrido Hueco(string pasillo, string columna, string fila)
            => new HuecoRecorrido { Pasillo = pasillo, Columna = columna, Fila = fila };

        private static CalleRecorridoDTO Calle(string nombre, string sentido, params string[] pasillos) => new CalleRecorridoDTO
        {
            Calle = nombre,
            Sentido = sentido,
            Pasillos = pasillos.Select(p => new PasilloRecorridoAlmacenDTO { Pasillo = p }).ToList()
        };

        // ------------------------------------------------------------------ controlador

        private static AlmacenController Controller(IServicioRecorridoAlmacen recorrido, params string[] grupos)
        {
            return new AlmacenController(A.Fake<IServicioPreparacionAlmacen>(), A.Fake<IServicioUbicacionesAlmacen>(),
                A.Fake<IServicioRecepcionCompras>(), A.Fake<IServicioRecepcionReposiciones>(), recorrido: recorrido)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Pedro", "Bearer"), grupos),
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
        }

        [TestMethod]
        public void Controlador_EscribirPideAlmacenODireccion_ComoEtiquetasDeHueco()
        {
            Assert.IsTrue(typeof(AlmacenController).GetCustomAttributes(typeof(EscrituraSoloAlmacenAttribute), true).Length > 0);
        }

        [TestMethod]
        public async Task GetRecorrido_PideElAlmacenEnMayusculasYLaEmpresaUno()
        {
            var servicio = A.Fake<IServicioRecorridoAlmacen>();
            var vista = new RecorridoAlmacenDTO { Almacen = "ALG" };
            A.CallTo(() => servicio.Leer("1", "ALG")).Returns(vista);

            IHttpActionResult respuesta = await Controller(servicio).GetRecorrido("alg");

            Assert.AreSame(vista, ((OkNegotiatedContentResult<RecorridoAlmacenDTO>)respuesta).Content);
        }

        [TestMethod]
        public async Task GetRecorrido_SinServicio_503()
        {
            IHttpActionResult respuesta = await Controller(null).GetRecorrido();

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)respuesta).StatusCode);
        }

        [TestMethod]
        public async Task PutRecorrido_SinPermisoDeAlmacen_403YNoGuarda()
        {
            var servicio = A.Fake<IServicioRecorridoAlmacen>();

            IHttpActionResult respuesta = await Controller(servicio, @"NUEVAVISION\Comerciales").PutRecorrido(new PeticionRecorridoAlmacenDTO());

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<string>)respuesta).StatusCode);
            A.CallTo(() => servicio.Guardar(A<string>._, A<string>._, A<PeticionRecorridoAlmacenDTO>._, A<string>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutRecorrido_Almacen_GuardaConElUsuarioYElEnsayo()
        {
            var servicio = A.Fake<IServicioRecorridoAlmacen>();
            var peticion = new PeticionRecorridoAlmacenDTO();
            var vista = new RecorridoAlmacenDTO { Ensayo = true };
            A.CallTo(() => servicio.Guardar("1", "ALG", peticion, A<string>.That.Contains("Pedro"), true)).Returns(vista);

            IHttpActionResult respuesta = await Controller(servicio, @"NUEVAVISION\Almacén").PutRecorrido(peticion, "ALG", ensayo: true);

            Assert.AreSame(vista, ((OkNegotiatedContentResult<RecorridoAlmacenDTO>)respuesta).Content);
        }

        [TestMethod]
        public async Task PutRecorrido_Direccion_TambienPuede()
        {
            var servicio = A.Fake<IServicioRecorridoAlmacen>();

            IHttpActionResult respuesta = await Controller(servicio, @"NUEVAVISION\Dirección").PutRecorrido(new PeticionRecorridoAlmacenDTO());

            Assert.IsInstanceOfType(respuesta, typeof(OkNegotiatedContentResult<RecorridoAlmacenDTO>));
        }

        [TestMethod]
        public async Task PutRecorrido_LoQueNoVale_400ConElMotivo()
        {
            var servicio = A.Fake<IServicioRecorridoAlmacen>();
            A.CallTo(() => servicio.Guardar(A<string>._, A<string>._, A<PeticionRecorridoAlmacenDTO>._, A<string>._, A<bool>._))
                .ThrowsAsync(new ArgumentException("La calle «a» está repetida."));

            IHttpActionResult respuesta = await Controller(servicio, @"NUEVAVISION\Almacén").PutRecorrido(new PeticionRecorridoAlmacenDTO());

            Assert.AreEqual("La calle «a» está repetida.", ((BadRequestErrorMessageResult)respuesta).Message);
        }

        [TestMethod]
        public async Task PutRecorrido_SinTablas_503()
        {
            var servicio = A.Fake<IServicioRecorridoAlmacen>();
            A.CallTo(() => servicio.Guardar(A<string>._, A<string>._, A<PeticionRecorridoAlmacenDTO>._, A<string>._, A<bool>._))
                .ThrowsAsync(new RecorridoSinTablasException());

            IHttpActionResult respuesta = await Controller(servicio, @"NUEVAVISION\Almacén").PutRecorrido(new PeticionRecorridoAlmacenDTO());

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ((NegotiatedContentResult<string>)respuesta).StatusCode);
        }

        // ------------------------------------------------------------------ servicio

        [TestMethod]
        public async Task Leer_SinTablas_NoLasTocaYElOrdenEsElDeSiempre()
        {
            var repositorio = A.Fake<IRepositorioRecorrido>();
            A.CallTo(() => repositorio.TablasCreadas()).Returns(false);
            A.CallTo(() => repositorio.LeerHuecosEnUso("1", "ALG")).Returns(new List<HuecoRecorrido>
            {
                Hueco("002", "001", "004"), Hueco("001", "002", "001"), Hueco("001", "001", "005")
            });

            RecorridoAlmacenDTO vista = await new ServicioRecorridoAlmacen(repositorio).Leer("1", "ALG");

            Assert.IsFalse(vista.TablasCreadas);
            Assert.AreEqual(4, vista.FilaAMano);
            A.CallTo(() => repositorio.LeerCalles(A<string>._, A<string>._)).MustNotHaveHappened();
            CollectionAssert.AreEqual(new[] { "001/001", "001/002", "002/001" }, vista.Tramos.Select(t => $"{t.Pasillo}/{t.Columna}").ToArray());
            Assert.IsTrue(vista.Tramos.All(t => t.Nivel == null && t.Calle == null));
            CollectionAssert.AreEqual(new[] { "001", "002" }, vista.PasillosSinCalle.Select(p => p.Pasillo).ToArray());
            Assert.AreEqual(2, vista.PasillosSinCalle[0].Huecos);
        }

        [TestMethod]
        public async Task Guardar_LoQueNoVale_NoTocaLaBaseDeDatos()
        {
            var repositorio = A.Fake<IRepositorioRecorrido>();
            var peticion = new PeticionRecorridoAlmacenDTO { Calles = { Calle("a", "A", "001"), Calle("A", "A", "002") } };

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => new ServicioRecorridoAlmacen(repositorio).Guardar("1", "ALG", peticion, "Pedro", false));

            A.CallTo(() => repositorio.Guardar(A<string>._, A<string>._, A<RecorridoNormalizado>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Guardar_SinTablas_Excepcion()
        {
            var repositorio = A.Fake<IRepositorioRecorrido>();
            A.CallTo(() => repositorio.TablasCreadas()).Returns(false);

            await Assert.ThrowsExceptionAsync<RecorridoSinTablasException>(() =>
                new ServicioRecorridoAlmacen(repositorio).Guardar("1", "ALG", new PeticionRecorridoAlmacenDTO(), "Pedro", false));
        }

        [TestMethod]
        public async Task Guardar_NormalizaYGuardaEnElOrdenDeLaListaYDevuelveLoGuardado()
        {
            var repositorio = A.Fake<IRepositorioRecorrido>();
            A.CallTo(() => repositorio.TablasCreadas()).Returns(true);
            RecorridoNormalizado guardado = null;
            A.CallTo(() => repositorio.Guardar("1", "ALG", A<RecorridoNormalizado>._, "Pedro"))
                .Invokes((string e, string a, RecorridoNormalizado r, string u) => guardado = r);
            var peticion = new PeticionRecorridoAlmacenDTO
            {
                FilaAMano = 5,
                Calles = { Calle(" a ", null, "1", "2"), Calle("d", "d", "4", "3") },
                PasillosSinCalle =
                {
                    new PasilloRecorridoAlmacenDTO { Pasillo = "005", Nombre = " Esmaltes " },
                    new PasilloRecorridoAlmacenDTO { Pasillo = "020" } // sin nombre: no se guarda
                }
            };

            RecorridoAlmacenDTO vista = await new ServicioRecorridoAlmacen(repositorio).Guardar("1", "ALG", peticion, "Pedro", false);

            Assert.AreEqual(5, guardado.FilaAMano);
            CollectionAssert.AreEqual(new[] { "a", "d" }, guardado.Calles.Select(c => c.Calle).ToArray());
            CollectionAssert.AreEqual(new[] { 10, 20 }, guardado.Calles.Select(c => c.Orden).ToArray());
            CollectionAssert.AreEqual(new[] { "A", "D" }, guardado.Calles.Select(c => c.Sentido).ToArray());
            CollectionAssert.AreEqual(new[] { "004", "003" }, guardado.Calles[1].Pasillos.Select(p => p.Pasillo).ToArray());
            CollectionAssert.AreEqual(new int?[] { 1, 2 }, guardado.Calles[1].Pasillos.Select(p => p.Posicion).ToArray());
            Assert.AreEqual(1, guardado.PasillosSinCalle.Count);
            Assert.AreEqual("Esmaltes", guardado.PasillosSinCalle[0].Nombre);
            Assert.IsFalse(vista.Ensayo);
            A.CallTo(() => repositorio.LeerCalles("1", "ALG")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Guardar_Ensayo_NoGuardaYEnseñaLosTramosDeLoPedido()
        {
            var repositorio = A.Fake<IRepositorioRecorrido>();
            A.CallTo(() => repositorio.TablasCreadas()).Returns(true);
            A.CallTo(() => repositorio.LeerFilaAMano("1", "ALG")).Returns(4);
            A.CallTo(() => repositorio.LeerHuecosEnUso("1", "ALG")).Returns(new List<HuecoRecorrido>
            {
                Hueco("001", "001", "004"), Hueco("001", "001", "001"), Hueco("002", "001", "005"), Hueco("002", "001", "002"),
                Hueco("001", "002", "004"), Hueco("030", "001", "001")
            });
            var peticion = new PeticionRecorridoAlmacenDTO { Calles = { Calle("a", "A", "001", "002") } };

            RecorridoAlmacenDTO vista = await new ServicioRecorridoAlmacen(repositorio).Guardar("1", "ALG", peticion, "Pedro", true);

            A.CallTo(() => repositorio.Guardar(A<string>._, A<string>._, A<RecorridoNormalizado>._, A<string>._)).MustNotHaveHappened();
            Assert.IsTrue(vista.Ensayo);
            Assert.IsNull(vista.Calles[0].Id);
            Assert.AreEqual(10, vista.Calles[0].Orden);
            // Columna 001: a mano el 1 y el 2; luego con la escalera el 1 y el 2; luego la columna 002; al final lo que no tiene calle
            CollectionAssert.AreEqual(new[]
            {
                "a 001/001 AMano", "a 002/001 AMano", "a 001/001 Escalera", "a 002/001 Escalera", "a 001/002 AMano", " 030/001 "
            }, vista.Tramos.Select(t => $"{t.Calle} {t.Pasillo}/{t.Columna} {t.Nivel}").ToArray());
            CollectionAssert.AreEqual(new[] { "030" }, vista.PasillosSinCalle.Select(p => p.Pasillo).ToArray());
            Assert.AreEqual(3, vista.Calles[0].Pasillos[0].Huecos);
        }

        // ------------------------------------------------------------------ reglas

        [TestMethod]
        public void Validar_ElMismoPasilloEnDosCalles_ArgumentException()
        {
            var peticion = new PeticionRecorridoAlmacenDTO { Calles = { Calle("a", "A", "1"), Calle("b", "A", "001") } };

            ArgumentException ex = Assert.ThrowsException<ArgumentException>(() => ReglasMantenimientoRecorrido.Validar(peticion));
            StringAssert.Contains(ex.Message, "001");
        }

        [TestMethod]
        public void Validar_LoQueNoVale()
        {
            Assert.ThrowsException<ArgumentException>(() => ReglasMantenimientoRecorrido.Validar(null));
            Assert.ThrowsException<ArgumentException>(() => ReglasMantenimientoRecorrido.Validar(new PeticionRecorridoAlmacenDTO { FilaAMano = 0 }));
            Assert.ThrowsException<ArgumentException>(() => ReglasMantenimientoRecorrido.Validar(new PeticionRecorridoAlmacenDTO { Calles = { Calle("a", "X", "1") } }));
            Assert.ThrowsException<ArgumentException>(() => ReglasMantenimientoRecorrido.Validar(new PeticionRecorridoAlmacenDTO { Calles = { Calle(" ", "A", "1") } }));
            Assert.ThrowsException<ArgumentException>(() => ReglasMantenimientoRecorrido.Validar(new PeticionRecorridoAlmacenDTO { Calles = { Calle("calle-muy-larga", "A", "1") } }));
            Assert.ThrowsException<ArgumentException>(() => ReglasMantenimientoRecorrido.Validar(new PeticionRecorridoAlmacenDTO { Calles = { Calle("a", "A", "1234") } }));
            Assert.ThrowsException<ArgumentException>(() => ReglasMantenimientoRecorrido.Validar(new PeticionRecorridoAlmacenDTO { Calles = { Calle("a", "A", "") } }));
            Assert.ThrowsException<ArgumentException>(() => ReglasMantenimientoRecorrido.Validar(new PeticionRecorridoAlmacenDTO
            {
                PasillosSinCalle = { new PasilloRecorridoAlmacenDTO { Pasillo = "5", Nombre = new string('x', 51) } }
            }));
        }

        [TestMethod]
        public void Validar_SinCalles_VaciaElRecorrido()
        {
            RecorridoNormalizado r = ReglasMantenimientoRecorrido.Validar(new PeticionRecorridoAlmacenDTO { Calles = null, PasillosSinCalle = null });

            Assert.AreEqual(0, r.Calles.Count);
            Assert.IsNull(r.FilaAMano);
        }

        [TestMethod]
        public void ConstruirVista_DeVuelta_ColumnasDescendentesYLosPasillosEnSuPosicion()
        {
            var calles = new[] { new CalleRecorridoGuardada { Id = 7, Calle = "h", Orden = 80, Sentido = "D" } };
            var pasillos = new[]
            {
                new PasilloRecorrido { Pasillo = "008", IdCalle = 7, Posicion = 2 },
                new PasilloRecorrido { Pasillo = "009", IdCalle = 7, Posicion = 1, Nombre = "Esmaltes" }
            };
            var huecos = new[] { Hueco("008", "001", "005"), Hueco("009", "002", "005"), Hueco("008", "002", "006") };

            RecorridoAlmacenDTO vista = ReglasMantenimientoRecorrido.ConstruirVista("1", "ALG", true, calles, pasillos, 4, huecos);

            CollectionAssert.AreEqual(new[] { "009", "008" }, vista.Calles[0].Pasillos.Select(p => p.Pasillo).ToArray());
            Assert.AreEqual("Esmaltes", vista.Calles[0].Pasillos[0].Nombre);
            Assert.AreEqual(7, vista.Calles[0].Id);
            CollectionAssert.AreEqual(new[] { "009/002", "008/002", "008/001" }, vista.Tramos.Select(t => $"{t.Pasillo}/{t.Columna}").ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, vista.Tramos.Select(t => t.Orden).ToArray());
            Assert.AreEqual(0, vista.PasillosSinCalle.Count);
        }
    }
}
