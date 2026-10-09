using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#553 (fase 3, lectura): recibir una reposición entre almacenes leyendo los productos.
    /// Hoy las diferencias entre lo enviado y lo recibido no quedan en ningún sitio.
    /// </summary>
    [TestClass]
    public class ServicioRecepcionReposicionesTests
    {
        private const int TRASPASO = 80841;
        private IRepositorioRecepcionReposiciones repositorio;
        private ServicioRecepcionReposiciones servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioRecepcionReposiciones>();
            servicio = new ServicioRecepcionReposiciones(repositorio);
            A.CallTo(() => repositorio.LeerLineas("1", "ALG", TRASPASO)).Returns(new List<FilaReposicion>
            {
                new FilaReposicion { Producto = "44194 ", Descripcion = "PLANCHA ", CodigoBarras = "8436620930427 ", Cantidad = 2 },
                new FilaReposicion { Producto = "LIMA", Descripcion = "LIMA", CodigoBarras = null, Cantidad = 10 }
            });
        }

        [TestMethod]
        public async Task LeerRecepcion_DevuelveLasLineasLimpiasYMarcaLasQueNoTienenCodigo()
        {
            RecepcionReposicionDTO recepcion = await servicio.LeerRecepcion("1", "ALG", TRASPASO);

            Assert.AreEqual(TRASPASO, recepcion.Traspaso);
            Assert.AreEqual("8436620930427", recepcion.Lineas.Single(l => l.Producto == "44194").CodigoBarras);
            Assert.IsTrue(recepcion.Lineas.Single(l => l.Producto == "LIMA").SinCodigo);
        }

        [TestMethod]
        public async Task LeerRecepcion_ReposicionDeOtroAlmacenOInexistente_Null()
        {
            A.CallTo(() => repositorio.LeerLineas("1", "REI", TRASPASO)).Returns(new List<FilaReposicion>());

            Assert.IsNull(await servicio.LeerRecepcion("1", "REI", TRASPASO));
        }

        // NestoAPI#577 (Carlos, 09/10/26): «La repo se rellena en Algete para enviar a Reina. Solo se debería ver en Algete en
        // ese momento. Cuando se contabiliza, se deja de ver en Algete y se comienza a ver en Reina.»

        [TestMethod]
        public async Task LeerRecepcion_SalidaDelOrigenSinContabilizar_409ConElOrigen()
        {
            A.CallTo(() => repositorio.LeerLineas("1", "REI", 80932)).Returns(new List<FilaReposicion>());
            A.CallTo(() => repositorio.LeerOrigenSinSalir("1", "REI", 80932)).Returns("Algete");

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.LeerRecepcion("1", "REI", 80932));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
            StringAssert.StartsWith(ex.Message, "La reposición 80932 todavía no ha salido de Algete");
        }

        [TestMethod]
        public async Task Casar_SalidaDelOrigenSinContabilizar_409()
        {
            A.CallTo(() => repositorio.LeerLineas("1", "ALC", 80938)).Returns(new List<FilaReposicion>());
            A.CallTo(() => repositorio.LeerOrigenSinSalir("1", "ALC", 80938)).Returns("Algete");

            NestoBusinessException ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                servicio.Casar("1", "ALC", 80938, new[] { new LecturaRecepcionDTO { Producto = "44194", Cantidad = 1 } }));

            Assert.AreEqual(HttpStatusCode.Conflict, ex.StatusCode);
        }

        [TestMethod]
        public async Task LeerRecepcion_SalidaYaContabilizada_SeVeComoSiempreSinMirarElOrigen()
        {
            // Las líneas ya filtran (SalidaReposicionSql.YA_HA_SALIDO): si las hay, la salida está contabilizada
            RecepcionReposicionDTO recepcion = await servicio.LeerRecepcion("1", "ALG", TRASPASO);

            Assert.AreEqual(2, recepcion.Lineas.Count);
            A.CallTo(() => repositorio.LeerOrigenSinSalir(A<string>.Ignored, A<string>.Ignored, A<int>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeerRecepcion_ReposicionDeTiendaYaRecibidaOInexistente_NullNo409()
        {
            // Las de las tiendas contabilizan la salida al terminarlas: no queda salida pendiente y es un 404 normal
            A.CallTo(() => repositorio.LeerLineas("1", "REI", 80940)).Returns(new List<FilaReposicion>());
            A.CallTo(() => repositorio.LeerOrigenSinSalir("1", "REI", 80940)).Returns((string)null);

            Assert.IsNull(await servicio.LeerRecepcion("1", "REI", 80940));
        }

        [TestMethod]
        public async Task ComprobarQueHaSalido_SinSalidaPendiente_NoLanza()
        {
            A.CallTo(() => repositorio.LeerOrigenSinSalir("1", "REI", 80931)).Returns((string)null);

            await servicio.ComprobarQueHaSalido("1", "REI", 80931);
        }

        [TestMethod]
        public void Consultas_TodosLosLectoresDePendientesDeRecibir_ExcluyenLasQueNoHanSalido()
        {
            // Un solo predicado para la lista (Nesto, Ariadna/Entradas), las líneas (abrir, casar, PDF, terminar) y el
            // diario que se contabiliza al terminar
            StringAssert.Contains(RepositorioRecepcionReposiciones.SQL_PENDIENTES, SalidaReposicionSql.YA_HA_SALIDO);
            StringAssert.Contains(RepositorioRecepcionReposiciones.SQL_LINEAS, SalidaReposicionSql.YA_HA_SALIDO);
            StringAssert.Contains(TransaccionCierreReposicionSql.SQL_TRASPASOS_EN_DIARIO, SalidaReposicionSql.YA_HA_SALIDO);
            StringAssert.Contains(SalidaReposicionSql.SQL_ORIGEN_SIN_SALIR, "NOT " + SalidaReposicionSql.YA_HA_SALIDO);
            // La salida que cuenta: líneas negativas del traspaso en el diario de salida de reposiciones del origen
            StringAssert.Contains(SalidaReposicionSql.YA_HA_SALIDO, "so.DiarioSalidaRep = s.Diario");
            StringAssert.Contains(SalidaReposicionSql.YA_HA_SALIDO, "s.[Almacén] = p.[Delegación]");
            StringAssert.Contains(SalidaReposicionSql.YA_HA_SALIDO, "s.Cantidad < 0");
        }

        [TestMethod]
        public async Task Casar_LlegaLoEnviado_Cuadra()
        {
            ResultadoRecepcionReposicionDTO resultado = await servicio.Casar("1", "ALG", TRASPASO, new[]
            {
                new LecturaRecepcionDTO { Producto = "44194", Cantidad = 2 },
                new LecturaRecepcionDTO { Producto = "LIMA", Cantidad = 10 }
            });

            Assert.IsTrue(resultado.Cuadra);
        }

        [TestMethod]
        public async Task Casar_FaltaUnaUnidadYLlegaAlgoQueNoIba_QuedaALaVista()
        {
            ResultadoRecepcionReposicionDTO resultado = await servicio.Casar("1", "ALG", TRASPASO, new[]
            {
                new LecturaRecepcionDTO { Producto = "44194", Cantidad = 1 },
                new LecturaRecepcionDTO { Producto = "LIMA", Cantidad = 10 },
                new LecturaRecepcionDTO { Producto = "18004", Cantidad = 1 }
            });

            Assert.IsFalse(resultado.Cuadra);
            Assert.AreEqual(-1, resultado.Productos.Single(p => p.Producto == "44194").Diferencia);
            Assert.IsTrue(resultado.Productos.Single(p => p.Producto == "18004").Ajeno);
        }
    }
}
