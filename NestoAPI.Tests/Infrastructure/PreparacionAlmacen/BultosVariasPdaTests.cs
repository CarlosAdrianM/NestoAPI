using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#646: varias PDA cerrando bultos de la misma entrega. El nombre del blob es el de cada subida (su IdCliente), nunca
    /// el número de bulto pedido; y si SQL Server elige la fila como víctima de un interbloqueo (1205), se reintenta en el servidor.
    /// </summary>
    [TestClass]
    public class BultosVariasPdaTests
    {
        private const string EMPRESA = "1";
        private const int PICKING = 99633;
        private const int PEDIDO = 926940;
        private static readonly byte[] JPEG = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

        private IRepositorioPreparacionAlmacen repositorio;
        private IAlmacenFotosBultos fotos;
        private ServicioPreparacionAlmacen servicio;
        private readonly List<string> subidas = new List<string>();

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            fotos = A.Fake<IAlmacenFotosBultos>();
            A.CallTo(() => fotos.Configurado).Returns(true);
            A.CallTo(() => fotos.Subir(A<string>._, A<byte[]>._, A<string>._))
                .ReturnsLazily((string ruta, byte[] b, string t) => { subidas.Add(ruta); return Task.CompletedTask; });
            A.CallTo(() => repositorio.LeerBultoPorIdCliente(A<Guid>._)).Returns(Task.FromResult<BultoAlmacenDTO>(null));
            A.CallTo(() => repositorio.ExistePedidoEnPicking(EMPRESA, PEDIDO, PICKING)).Returns(true);
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._))
                .ReturnsLazily((BultoAlmacenDTO b, string h, int t, string d) => Task.FromResult(b));
            servicio = new ServicioPreparacionAlmacen(repositorio, fotos) { RetrasoReintentoBultoMs = 0 };
        }

        private static FotoBultoAlmacen Foto(string dispositivo, Guid? id = null) => new FotoBultoAlmacen
        {
            IdCliente = id ?? Guid.NewGuid(),
            Empresa = EMPRESA,
            Pedido = PEDIDO,
            Picking = PICKING,
            Bulto = 2,
            Imagen = JPEG,
            FechaFoto = new DateTime(2026, 10, 20, 12, 0, 0), // las dos en el mismo segundo
            Dispositivo = dispositivo
        };

        [TestMethod]
        public async Task DosPdaCierranElMismoNumero_CadaFotoVaASuBlob()
        {
            FotoBultoAlmacen andre = Foto("PDA-1");
            FotoBultoAlmacen pedro = Foto("PDA-2");

            BultoAlmacenDTO deAndre = await servicio.GuardarFotoBulto(andre, "Andre");
            BultoAlmacenDTO dePedro = await servicio.GuardarFotoBulto(pedro, "Pedro");

            Assert.AreEqual(2, subidas.Distinct().Count(), "Ninguna foto pisa a la otra");
            Assert.AreEqual($"1/926940/99633/bulto-{andre.IdCliente:N}.jpg", deAndre.RutaBlob);
            Assert.AreEqual($"1/926940/99633/bulto-{pedro.IdCliente:N}.jpg", dePedro.RutaBlob);
            Assert.IsFalse(deAndre.RutaBlob.Contains("bulto-2-"), "El número de bulto no va en el nombre (lo dice la fila)");
        }

        [TestMethod]
        public async Task ReenvioDelMismoIdCliente_ElMismoBlobYLaMismaFila()
        {
            // La primera vez la foto sube pero la fila no se guarda (se corta); la cola reenvía con el mismo IdCliente
            Guid id = Guid.NewGuid();
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._))
                .Throws(new InvalidOperationException("se corta")).Once();
            _ = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => servicio.GuardarFotoBulto(Foto("PDA-1", id), "Andre"));

            BultoAlmacenDTO guardado = await servicio.GuardarFotoBulto(Foto("PDA-1", id), "Andre");

            Assert.AreEqual(2, subidas.Count);
            Assert.AreEqual(subidas[0], subidas[1], "El reenvío va al mismo blob: no deja fotos huérfanas con otro nombre");
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>.That.Matches(b => b.IdCliente == id && b.RutaBlob == subidas[0]),
                A<string>._, A<int>._, A<string>._)).MustHaveHappenedTwiceExactly();
            Assert.AreEqual(id, guardado.IdCliente, "La misma fila (el mismo IdCliente)");
        }

        [TestMethod]
        public async Task ReenvioConLaFilaYaGuardada_NiSubeNiGuardaOtraVez()
        {
            Guid id = Guid.NewGuid();
            var fila = new BultoAlmacenDTO { Id = 7, IdCliente = id, Bulto = 2, RutaBlob = $"1/926940/99633/bulto-{id:N}.jpg" };
            A.CallTo(() => repositorio.LeerBultoPorIdCliente(id)).Returns(fila);

            BultoAlmacenDTO guardado = await servicio.GuardarFotoBulto(Foto("PDA-1", id), "Andre");

            Assert.AreSame(fila, guardado);
            Assert.AreEqual(0, subidas.Count);
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task UnInterbloqueoEnElPrimerIntento_ElSegundoVaBienYLaFotoSubeUnaVez()
        {
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._))
                .Throws(new Exception("envuelto", CrearSqlException(1205))).Once();

            BultoAlmacenDTO guardado = await servicio.GuardarFotoBulto(Foto("PDA-1"), "Andre");

            Assert.AreEqual(2, guardado.Bulto);
            Assert.AreEqual(1, subidas.Count, "La foto se sube una vez, fuera de la transacción que se reintenta");
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task TresInterbloqueos_ErrorClaroQueDejaReintentar()
        {
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._))
                .Throws(() => CrearSqlException(1205));

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.GuardarFotoBulto(Foto("PDA-1"), "Andre"));

            Assert.AreEqual(ServicioPreparacionAlmacen.MENSAJE_BULTO_INTERBLOQUEADO, ex.Message);
            Assert.AreEqual(System.Net.HttpStatusCode.InternalServerError, ex.StatusCode, "500: Ariadna se queda la foto para Reintentar");
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._)).MustHaveHappened(3, Times.Exactly);
        }

        [TestMethod]
        public async Task OtroErrorSql_NoSeReintenta()
        {
            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._))
                .Throws(() => CrearSqlException(2627));

            _ = await Assert.ThrowsExceptionAsync<SqlException>(() => servicio.GuardarFotoBulto(Foto("PDA-1"), "Andre"));

            A.CallTo(() => repositorio.GuardarBultoSinPisar(A<BultoAlmacenDTO>._, A<string>._, A<int>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task LasFotosDeAntes_SeSiguenViendoPorElNombreDeLaFila()
        {
            // Una foto de antes de #646: el nombre llevaba el número y la fecha. Se lee el de la fila, nunca se reconstruye
            const string antigua = "1/926940/99633/bulto-1-20260930120000.jpg";
            A.CallTo(() => repositorio.LeerBulto(17)).Returns(new BultoAlmacenDTO { Id = 17, Bulto = 1, RutaBlob = antigua });
            A.CallTo(() => fotos.EnlaceDeLectura(antigua, A<TimeSpan>._)).Returns(new Uri("https://fotos/antigua?sig=x"));

            Uri enlace = await servicio.EnlaceFotoBulto(17);

            Assert.AreEqual("https://fotos/antigua?sig=x", enlace.AbsoluteUri);
        }

        private static SqlException CrearSqlException(int number)
        {
            ConstructorInfo ctorError = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .First(c => c.GetParameters().Length >= 7 && c.GetParameters()[0].ParameterType == typeof(int));
            object[] argsError = ctorError.GetParameters().Select((p, i) =>
            {
                if (i == 0) return (object)number;
                if (p.ParameterType == typeof(string)) return "test";
                if (p.ParameterType == typeof(byte)) return (byte)0;
                if (p.ParameterType == typeof(int)) return 0;
                if (p.ParameterType == typeof(uint)) return 0u;
                return null;
            }).ToArray();
            var error = (SqlError)ctorError.Invoke(argsError);
            var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true);
            _ = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(collection, new object[] { error });
            MethodInfo crear = typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(SqlErrorCollection), typeof(string) }, null);
            return (SqlException)crear.Invoke(null, new object[] { collection, "11.0.0" });
        }
    }
}
