using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
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
