using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>Ariadna#43: lecturas que no casan y por qué se teclea un producto con código.</summary>
    [TestClass]
    public class LecturasAlmacenTests
    {
        private IRepositorioLecturasAlmacen repositorio;
        private ServicioLecturasAlmacen servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioLecturasAlmacen>();
            servicio = new ServicioLecturasAlmacen(repositorio);
        }

        [TestMethod]
        public async Task RegistrarNoCasadas_GuardaLasQueTraenCodigoYDescartaElResto()
        {
            IList<LecturaNoCasadaDTO> guardadas = null;
            A.CallTo(() => repositorio.InsertarNoCasadas("1", A<IList<LecturaNoCasadaDTO>>._, "Andre"))
                .Invokes((string _, IList<LecturaNoCasadaDTO> l, string __) => guardadas = l)
                .ReturnsLazily((string _, IList<LecturaNoCasadaDTO> l, string __) => l.Count);

            ResultadoLecturasNoCasadasDTO resultado = await servicio.RegistrarNoCasadas("1", new List<LecturaNoCasadaDTO>
            {
                new LecturaNoCasadaDTO { Codigo = " 8412345678905 ", ProductoEsperado = "44726", Pantalla = "Recorrido", TipoOrigen = "repo", NumeroOrigen = 81040, ConEscaner = true, FechaLectura = new DateTime(2026, 10, 19, 10, 0, 0) },
                new LecturaNoCasadaDTO { Codigo = "  " },
                new LecturaNoCasadaDTO { Codigo = new string('9', 51) },
                null
            }, "Andre");

            Assert.AreEqual(1, resultado.Guardadas);
            Assert.AreEqual(2, resultado.Descartadas);
            LecturaNoCasadaDTO guardada = guardadas.Single();
            Assert.AreEqual("8412345678905", guardada.Codigo);
            Assert.AreEqual("REPO", guardada.TipoOrigen);
            Assert.AreEqual(new DateTime(2026, 10, 19, 10, 0, 0), guardada.FechaLectura);
        }

        [TestMethod]
        public async Task RegistrarNoCasadas_FechaDeLaPdaMal_LaDelServidor()
        {
            IList<LecturaNoCasadaDTO> guardadas = null;
            A.CallTo(() => repositorio.InsertarNoCasadas(A<string>._, A<IList<LecturaNoCasadaDTO>>._, A<string>._))
                .Invokes((string _, IList<LecturaNoCasadaDTO> l, string __) => guardadas = l).Returns(1);

            _ = await servicio.RegistrarNoCasadas("1", new List<LecturaNoCasadaDTO> { new LecturaNoCasadaDTO { Codigo = "123456" } }, "Andre");

            Assert.IsTrue(guardadas.Single().FechaLectura > DateTime.Now.AddMinutes(-1));
        }

        [TestMethod]
        public async Task RegistrarNoCasadas_SinNadaQueGuardar_NoVaALaBaseDeDatos()
        {
            ResultadoLecturasNoCasadasDTO resultado = await servicio.RegistrarNoCasadas("1", null, "Andre");

            Assert.AreEqual(0, resultado.Guardadas);
            A.CallTo(() => repositorio.InsertarNoCasadas(A<string>._, A<IList<LecturaNoCasadaDTO>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task RegistrarNoCasadas_DemasiadasEnUnEnvio_Rechaza()
        {
            _ = await servicio.RegistrarNoCasadas("1", Enumerable.Range(0, ServicioLecturasAlmacen.MAXIMO_POR_ENVIO + 1)
                .Select(i => new LecturaNoCasadaDTO { Codigo = "12345" + i }).ToList(), "Andre");
        }

        [TestMethod]
        public async Task LeerMotivo_SinFila_NoSeHaPreguntado()
        {
            A.CallTo(() => repositorio.LeerMotivo("1", "17404")).Returns(Task.FromResult<MotivoTecleoGuardado>(null));

            EstadoMotivoTecleoDTO estado = await servicio.LeerMotivo("1", " 17404 ");

            Assert.IsFalse(estado.Preguntado);
            Assert.AreEqual("17404", estado.Producto);
        }

        [TestMethod]
        public async Task LeerMotivo_ConFila_YaSeHaPreguntado()
        {
            A.CallTo(() => repositorio.LeerMotivo("1", "17404")).Returns(new MotivoTecleoGuardado
            {
                Producto = "17404", Motivo = "NOSELEE", Codigo = "8412345678905", FechaModificacion = new DateTime(2026, 10, 19)
            });

            EstadoMotivoTecleoDTO estado = await servicio.LeerMotivo("1", "17404");

            Assert.IsTrue(estado.Preguntado);
            Assert.AreEqual(MotivosTecleo.NO_SE_LEE, estado.Motivo);
            Assert.AreEqual("8412345678905", estado.Codigo);
        }

        [TestMethod]
        public async Task GuardarMotivo_NoSeLee_LoGuardaYDiceQueLoVeraCompras()
        {
            A.CallTo(() => repositorio.ExisteProducto("1", "17404")).Returns(true);
            A.CallTo(() => repositorio.LeerMotivo("1", "17404")).Returns(new MotivoTecleoGuardado { Producto = "17404", Motivo = "NOSELEE" });

            EstadoMotivoTecleoDTO estado = await servicio.GuardarMotivo("1",
                new MotivoTecleoDTO { Producto = "17404", Motivo = "noselee", Codigo = "8412345678905", Dispositivo = "EDA50K" }, "Andre");

            A.CallTo(() => repositorio.GuardarMotivo("1", "17404", "NOSELEE", "8412345678905", "Andre", "EDA50K")).MustHaveHappenedOnceExactly();
            Assert.IsTrue(estado.Preguntado);
            StringAssert.Contains(estado.Mensaje, "Compras");
        }

        [TestMethod]
        public async Task GuardarMotivo_MotivoRaro_Rechaza()
        {
            await AssertRechaza(new MotivoTecleoDTO { Producto = "17404", Motivo = "PORQUESI" });
            A.CallTo(() => repositorio.GuardarMotivo(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GuardarMotivo_ProductoQueNoExiste_Rechaza()
        {
            A.CallTo(() => repositorio.ExisteProducto("1", "99999")).Returns(false);

            await AssertRechaza(new MotivoTecleoDTO { Producto = "99999", Motivo = "OTRO" });
        }

        [TestMethod]
        public void Sql_EscribeEnLasTablasDelScript()
        {
            StringAssert.Contains(RepositorioLecturasAlmacenSql.SQL_INSERTAR_NO_CASADA, "INSERT INTO LecturasNoCasadasAlmacen");
            StringAssert.Contains(RepositorioLecturasAlmacenSql.SQL_GUARDAR_MOTIVO, "MERGE ProductosMotivoTecleo");
            StringAssert.Contains(RepositorioLecturasAlmacenSql.SQL_GUARDAR_MOTIVO, "Veces = t.Veces + 1");
        }

        private async Task AssertRechaza(MotivoTecleoDTO motivo)
        {
            try
            {
                _ = await servicio.GuardarMotivo("1", motivo, "Andre");
                Assert.Fail("Tenía que rechazarlo");
            }
            catch (ArgumentException)
            {
            }
        }
    }
}
