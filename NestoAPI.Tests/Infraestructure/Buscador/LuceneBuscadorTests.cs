using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Buscador;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static NestoAPI.Infraestructure.Buscador.LuceneBuscador;

namespace NestoAPI.Tests.Infraestructure.Buscador
{
    /// <summary>
    /// Tests del buscador sobre un índice temporal en disco: no tocan ni la base de datos ni el
    /// índice real de la aplicación. Cubren la issue #407 (indexar anulados con flag para que la
    /// tienda pueda mostrarlos, TiendasNuevaVision#38).
    /// </summary>
    [TestClass]
    public class LuceneBuscadorTests
    {
        private string _rutaIndice;

        [TestInitialize]
        public void Inicializar()
        {
            _rutaIndice = Path.Combine(Path.GetTempPath(), "NestoAPI.Tests.lucene", Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(_rutaIndice);
        }

        [TestCleanup]
        public void Limpiar()
        {
            try
            {
                if (Directory.Exists(_rutaIndice))
                {
                    Directory.Delete(_rutaIndice, true);
                }
            }
            catch (IOException)
            {
                // Si Windows aún tiene el fichero cogido no merece la pena tumbar el test por eso:
                // es una carpeta temporal.
            }
        }

        private static ResultadoBusqueda CrearProducto(string id, string nombre, bool anulado)
        {
            return new ResultadoBusqueda
            {
                Tipo = "producto",
                Id = id,
                Nombre = nombre,
                Familia = "Familia",
                Subgrupo = "Subgrupo",
                DescripcionBreve = "",
                DescripcionLarga = "",
                Anulado = anulado
            };
        }

        private void Indexar(params ResultadoBusqueda[] productos)
        {
            LuceneBuscador.Indexar(_rutaIndice, productos.ToList(), new List<(int, string, string, string)>());
        }

        private List<dynamic> Buscar(string texto, bool incluirAnulados)
        {
            return LuceneBuscador.BuscarEnIndice(_rutaIndice, new ParametrosBusqueda
            {
                Query = texto,
                IncluirAnulados = incluirAnulados
            });
        }

        private ResultadoPaginado BuscarPaginado(string texto, int skip, int take, bool incluirAnulados = false)
        {
            return LuceneBuscador.BuscarPaginadoEnIndice(_rutaIndice, new ParametrosBusqueda
            {
                Query = texto,
                Skip = skip,
                Take = take,
                IncluirAnulados = incluirAnulados
            });
        }

        [TestMethod]
        public void LuceneBuscador_Paginado_DevuelveElTotalYSoloLaPaginaPedida()
        {
            // El buscador de la tienda PrestaShop necesita saber si hay más resultados
            Indexar(
                CrearProducto("1", "Champú uno", anulado: false),
                CrearProducto("2", "Champú dos", anulado: false),
                CrearProducto("3", "Champú tres", anulado: false),
                CrearProducto("4", "Champú cuatro", anulado: false),
                CrearProducto("5", "Champú cinco", anulado: false));

            ResultadoPaginado primera = BuscarPaginado("champú", skip: 0, take: 2);
            ResultadoPaginado segunda = BuscarPaginado("champú", skip: 2, take: 2);
            ResultadoPaginado ultima = BuscarPaginado("champú", skip: 4, take: 2);

            Assert.AreEqual(5, primera.Total);
            Assert.AreEqual(2, primera.Resultados.Count);
            Assert.AreEqual(5, segunda.Total, "el total no depende de la página");
            Assert.AreEqual(2, segunda.Resultados.Count);
            Assert.AreEqual(1, ultima.Resultados.Count, "la última página va incompleta");
            Assert.AreEqual(0, primera.TotalAnulados);

            HashSet<string> ids = new HashSet<string>();
            foreach (dynamic r in primera.Resultados) { ids.Add((string)r.Id); }
            foreach (dynamic r in segunda.Resultados) { ids.Add((string)r.Id); }
            foreach (dynamic r in ultima.Resultados) { ids.Add((string)r.Id); }
            Assert.AreEqual(5, ids.Count, "las páginas no se solapan ni se saltan nada");
        }

        [TestMethod]
        public void LuceneBuscador_Paginado_ElTotalCuentaActivosYLosAnuladosAparte()
        {
            Indexar(
                CrearProducto("1", "Champú uno", anulado: false),
                CrearProducto("2", "Champú dos", anulado: false),
                CrearProducto("3", "Champú descatalogado", anulado: true));

            ResultadoPaginado sinAnulados = BuscarPaginado("champú", skip: 0, take: 10, incluirAnulados: false);
            ResultadoPaginado conAnulados = BuscarPaginado("champú", skip: 0, take: 10, incluirAnulados: true);

            Assert.AreEqual(2, sinAnulados.Total);
            Assert.AreEqual(0, sinAnulados.TotalAnulados);
            Assert.AreEqual(2, sinAnulados.Resultados.Count);
            Assert.AreEqual(2, conAnulados.Total, "Total sigue siendo el de los activos");
            Assert.AreEqual(1, conAnulados.TotalAnulados);
            Assert.AreEqual(3, conAnulados.Resultados.Count);
        }

        [TestMethod]
        public void LuceneBuscador_Paginado_SinResultados_TotalCero()
        {
            Indexar(CrearProducto("1", "Champú uno", anulado: false));

            ResultadoPaginado resultado = BuscarPaginado("mascarilla", skip: 0, take: 10);

            Assert.AreEqual(0, resultado.Total);
            Assert.AreEqual(0, resultado.Resultados.Count);
        }

        [TestMethod]
        public void BuscadorController_Parametros_AcotaSkipYTake()
        {
            // Nadie vuelca el índice entero con take=100000, ni pide una página negativa
            ParametrosBusqueda grande = NestoAPI.Controllers.BuscadorController.Parametros("champú", null, false, -5, 100000);
            ParametrosBusqueda cero = NestoAPI.Controllers.BuscadorController.Parametros("champú", "producto", true, 40, 0);
            ParametrosBusqueda normal = NestoAPI.Controllers.BuscadorController.Parametros("champú", null, false, 20, 20);

            Assert.AreEqual(0, grande.Skip);
            Assert.AreEqual(NestoAPI.Controllers.BuscadorController.TAKE_MAXIMO, grande.Take);
            Assert.AreEqual(NestoAPI.Controllers.BuscadorController.TAKE_POR_DEFECTO, cero.Take);
            Assert.AreEqual(40, cero.Skip);
            Assert.AreEqual("producto", cero.Tipo);
            Assert.IsTrue(cero.IncluirAnulados);
            Assert.AreEqual(20, normal.Skip);
            Assert.AreEqual(20, normal.Take);
        }

        [TestMethod]
        public void LuceneBuscador_SiNoSePidenAnulados_NoSeDevuelven()
        {
            Indexar(
                CrearProducto("1", "Champú anticaspa", anulado: false),
                CrearProducto("2", "Champú descatalogado", anulado: true));

            List<dynamic> resultados = Buscar("champú", incluirAnulados: false);

            Assert.AreEqual(1, resultados.Count);
            Assert.AreEqual("1", resultados[0].Id);
            Assert.IsFalse(resultados[0].Anulado);
        }

        [TestMethod]
        public void LuceneBuscador_SiSePidenAnulados_SeDevuelvenMarcados()
        {
            Indexar(
                CrearProducto("1", "Champú anticaspa", anulado: false),
                CrearProducto("2", "Champú descatalogado", anulado: true));

            List<dynamic> resultados = Buscar("champú", incluirAnulados: true);

            Assert.AreEqual(2, resultados.Count);
            dynamic anulado = resultados.Single(r => r.Id == "2");
            Assert.IsTrue(anulado.Anulado);
        }

        [TestMethod]
        public void LuceneBuscador_SiSePidenAnulados_VanSiempreDetrasDeLosActivos()
        {
            // El anulado se indexa el primero y con el nombre que mejor casa con la búsqueda, así
            // que por relevancia pura saldría antes: la tienda necesita que salga detrás igualmente.
            Indexar(
                CrearProducto("2", "Champú", anulado: true),
                CrearProducto("1", "Champú anticaspa para cabello graso", anulado: false));

            List<dynamic> resultados = Buscar("champú", incluirAnulados: true);

            Assert.AreEqual(2, resultados.Count);
            Assert.AreEqual("1", resultados[0].Id, "Los activos van primero");
            Assert.AreEqual("2", resultados[1].Id, "Los anulados van detrás");
        }

        [TestMethod]
        public void LuceneBuscador_SiSoloHayAnuladosYNoSePiden_DevuelveVacio()
        {
            Indexar(CrearProducto("2", "Champú descatalogado", anulado: true));

            List<dynamic> resultados = Buscar("champú", incluirAnulados: false);

            Assert.AreEqual(0, resultados.Count);
        }

        [TestMethod]
        public void LuceneBuscador_LosVideosNoQuedanFueraAlFiltrarAnulados()
        {
            // Los vídeos no llevan el campo Anulado. Si el filtro de activos exigiera "Anulado:false"
            // en vez de excluir los anulados, los vídeos desaparecerían de la búsqueda.
            LuceneBuscador.Indexar(
                _rutaIndice,
                new List<ResultadoBusqueda> { CrearProducto("1", "Manicura permanente", anulado: false) },
                new List<(int, string, string, string)> { (99, "Protocolo de manicura", "transcripción", "Vídeo de manicura") });

            List<dynamic> resultados = Buscar("manicura", incluirAnulados: false);

            Assert.AreEqual(2, resultados.Count);
            Assert.IsTrue(resultados.Any(r => r.Tipo == "video"), "El vídeo debe seguir apareciendo");
        }

        [TestMethod]
        public void LuceneBuscador_SiSeFiltraPorTipoProducto_SigueRespetandoElFiltroDeAnulados()
        {
            LuceneBuscador.Indexar(
                _rutaIndice,
                new List<ResultadoBusqueda>
                {
                    CrearProducto("1", "Manicura permanente", anulado: false),
                    CrearProducto("2", "Manicura descatalogada", anulado: true)
                },
                new List<(int, string, string, string)> { (99, "Protocolo de manicura", "transcripción", "Vídeo de manicura") });

            List<dynamic> resultados = LuceneBuscador.BuscarEnIndice(_rutaIndice, new ParametrosBusqueda
            {
                Query = "manicura",
                Tipo = "producto",
                IncluirAnulados = false
            });

            Assert.AreEqual(1, resultados.Count);
            Assert.AreEqual("1", resultados[0].Id);
        }
    }
}
