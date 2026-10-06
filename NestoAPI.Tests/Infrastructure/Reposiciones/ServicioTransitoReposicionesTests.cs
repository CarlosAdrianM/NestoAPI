using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>Nesto#510: GET api/Reposiciones/EnTransito, lo que viaja hacia un almacén antes de cambiar el de un pedido.</summary>
    [TestClass]
    public class ServicioTransitoReposicionesTests
    {
        private IRepositorioTransitoReposiciones repositorio;
        private ServicioTransitoReposiciones servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioTransitoReposiciones>();
            servicio = new ServicioTransitoReposiciones(repositorio);
        }

        [TestMethod]
        public async Task LeerEnTransito_LimpiaLaListaDeProductosYPasaAlmacenEnMayusculas()
        {
            IReadOnlyList<string> pedidos = null;
            A.CallTo(() => repositorio.LeerEnTransito("1", "ALC", A<IReadOnlyList<string>>._))
                .Invokes((string e, string a, IReadOnlyList<string> p) => pedidos = p)
                .Returns(new List<ProductoEnTransitoDTO>());

            await servicio.LeerEnTransito("1", " alc ", " 45146, 45148,,45146 ");

            CollectionAssert.AreEqual(new[] { "45146", "45148" }, pedidos.ToArray());
        }

        [TestMethod]
        public async Task LeerEnTransito_DevuelveLasFilasLimpiasConTraspasoYOrigen()
        {
            A.CallTo(() => repositorio.LeerEnTransito("1", "ALC", A<IReadOnlyList<string>>._)).Returns(new List<ProductoEnTransitoDTO>
            {
                new ProductoEnTransitoDTO { Producto = "45146 ", Unidades = 4, Traspaso = 80885, Origen = "ALG " },
                new ProductoEnTransitoDTO { Producto = "45148", Unidades = 2, Traspaso = null, Origen = "REI" },
                new ProductoEnTransitoDTO { Producto = "99999", Unidades = 0, Traspaso = 80885, Origen = "ALG" }
            });

            List<ProductoEnTransitoDTO> filas = await servicio.LeerEnTransito("1", "ALC", "45146,45148,99999");

            Assert.AreEqual(2, filas.Count);
            ProductoEnTransitoDTO enviada = filas.Single(f => f.Producto == "45146");
            Assert.AreEqual(4, enviada.Unidades);
            Assert.AreEqual(80885, enviada.Traspaso);
            Assert.AreEqual("ALG", enviada.Origen);
            Assert.IsNull(filas.Single(f => f.Producto == "45148").Traspaso, "En preparación: sin número de traspaso");
        }

        [TestMethod]
        public async Task LeerEnTransito_SinProductos_ListaVaciaSinConsultar()
        {
            List<ProductoEnTransitoDTO> filas = await servicio.LeerEnTransito("1", "ALC", " , ");

            Assert.AreEqual(0, filas.Count);
            A.CallTo(() => repositorio.LeerEnTransito(A<string>._, A<string>._, A<IReadOnlyList<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeerEnTransito_SinAlmacen_Error()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.LeerEnTransito("1", " ", "45146"));
        }

        [TestMethod]
        public async Task LeerEnTransito_EmpresaVacia_UsaLaDePorDefecto()
        {
            await servicio.LeerEnTransito(null, "ALC", "45146");

            A.CallTo(() => repositorio.LeerEnTransito("1", "ALC", A<IReadOnlyList<string>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Sql_PendientesDeRecibirYEnPreparacion_SoloEntradasPositivasHaciaElAlmacen()
        {
            string sql = RepositorioTransitoReposicionesSql.Sql(3);

            StringAssert.Contains(sql, "p.[Número] IN (@prod0, @prod1, @prod2)");
            StringAssert.Contains(sql, "p.[Almacén] = @p1 AND p.Cantidad > 0");
            // Pendiente de recibir: con traspaso, en el diario de entrada del destino
            StringAssert.Contains(sql, "ISNULL(p.[NºTraspaso], 0) > 0");
            StringAssert.Contains(sql, "d.DiarioEntradaRep = p.Diario");
            // En preparación: sin traspaso, en el diario de salida de otro almacén, viva
            StringAssert.Contains(sql, "ISNULL(p.[NºTraspaso], 0) = 0 AND p.Estado >= 0");
            StringAssert.Contains(sql, "o.[Número] <> p.[Almacén] AND o.DiarioSalidaRep = p.Diario");
            StringAssert.Contains(sql, "RTRIM(p.[Delegación]) AS Origen");
            Assert.IsFalse(sql.Contains("UPDATE") || sql.Contains("DELETE") || sql.Contains("INSERT"), "Solo lectura");
        }
    }
}
