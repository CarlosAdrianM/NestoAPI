using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>Ariadna (03/10/26): lo que ve el mozo de cada producto además del nombre, resuelto en un solo sitio.</summary>
    [TestClass]
    public class FichasProductoAlmacenTests
    {
        private IRepositorioFichasProducto repositorio;
        private FichasProductoAlmacen fichas;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioFichasProducto>();
            fichas = new FichasProductoAlmacen(repositorio);
        }

        private void ConFichas(params FichaProductoAlmacen[] lista)
        {
            A.CallTo(() => repositorio.LeerFichas(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(lista.ToList());
        }

        [TestMethod]
        public async Task Completar_RellenaFamiliaSubgrupoTamanoYUnidad_AunqueElNumeroVengaConEspacios()
        {
            ConFichas(new FichaProductoAlmacen { Producto = "22624", Familia = "Starpil", Subgrupo = "Productos depilación ceras", Tamano = 500, UnidadMedida = "ml." });
            var linea = new LineaRecogidaDTO { Producto = "22624          " };

            await fichas.Completar("1", new[] { linea });

            Assert.AreEqual("Starpil", linea.Familia);
            Assert.AreEqual("Productos depilación ceras", linea.Subgrupo);
            Assert.AreEqual((short)500, linea.Tamano);
            Assert.AreEqual("ml.", linea.UnidadMedida);
        }

        [TestMethod]
        public async Task Completar_TambienLaFoto_AunqueElProductoNoTengaFicha()
        {
            IFotosProductoAlmacen fotos = A.Fake<IFotosProductoAlmacen>();
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>._)).Returns(new Dictionary<string, string>
            {
                ["22624"] = "https://tienda/22624-home_default/lata-cera-oro.jpg",
                ["18004"] = null
            });
            ConFichas();
            var conFoto = new LineaRecogidaDTO { Producto = "22624 " };
            var sinFoto = new LineaRecogidaDTO { Producto = "18004" };

            await new FichasProductoAlmacen(repositorio, fotos).Completar("1", new[] { conFoto, sinFoto });

            Assert.AreEqual("https://tienda/22624-home_default/lata-cera-oro.jpg", conFoto.UrlFoto);
            Assert.IsNull(sinFoto.UrlFoto);
            A.CallTo(() => fotos.Urls(A<IEnumerable<string>>.That.Matches(p => p.Count() == 2))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Completar_UnaSolaConsultaConCadaProductoUnaVez()
        {
            ConFichas();
            var productos = new IConFichaProducto[]
            {
                new LineaRecogidaDTO { Producto = "22624" },
                new LineaRecogidaDTO { Producto = "22624 " },
                new ProductoAlmacenDTO { Producto = "18004" }
            };

            await fichas.Completar("1", productos);

            A.CallTo(() => repositorio.LeerFichas("1", A<IReadOnlyCollection<string>>.That.Matches(p => p.Count == 2 && p.Contains("22624") && p.Contains("18004"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Completar_SinProductos_NoConsulta()
        {
            await fichas.Completar("1", new IConFichaProducto[] { new LineaRecepcionDTO { Producto = " " }, null });
            await fichas.Completar("1", null);

            A.CallTo(() => repositorio.LeerFichas(A<string>._, A<IReadOnlyCollection<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Completar_ProductoSinFicha_SeQuedaComoEstaba()
        {
            ConFichas();
            var linea = new LineaRecogidaDTO { Producto = "99999", Tamano = 250, UnidadMedida = "gr." };

            await fichas.Completar("1", new[] { linea });

            Assert.IsNull(linea.Familia);
            Assert.AreEqual((short)250, linea.Tamano);
            Assert.AreEqual("gr.", linea.UnidadMedida);
        }

        [TestMethod]
        public async Task Completar_LoVacioQuedaEnNull_YSinTamanoSeConservaElQueHabia()
        {
            ConFichas(new FichaProductoAlmacen { Producto = "18004", Familia = "  ", Subgrupo = "Cremas ", Tamano = null, UnidadMedida = "" });
            var producto = new ProductoPendienteDeUbicarDTO { Producto = "18004", Tamano = 50, UnidadMedida = "ml." };

            await fichas.Completar("1", new[] { producto });

            Assert.IsNull(producto.Familia);
            Assert.AreEqual("Cremas", producto.Subgrupo);
            Assert.AreEqual((short)50, producto.Tamano);
            Assert.AreEqual("ml.", producto.UnidadMedida);
        }
    }
}
