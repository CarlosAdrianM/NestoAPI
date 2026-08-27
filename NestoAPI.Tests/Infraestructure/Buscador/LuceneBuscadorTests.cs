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
