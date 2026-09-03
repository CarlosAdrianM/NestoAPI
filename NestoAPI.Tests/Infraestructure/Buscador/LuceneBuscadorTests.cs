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

        private static ResultadoBusqueda CrearProducto(string id, string nombre, bool anulado, int? posicionMasVendido = null)
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
                Anulado = anulado,
                PosicionMasVendido = posicionMasVendido
            };
        }

        private static List<string> Ids(IEnumerable<dynamic> resultados)
        {
            return resultados.Select(r => (string)r.Id).ToList();
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

        #region La palabra exacta gana al radical (03/09/26: "vapore" no sacaba la Vapore de Eva Visnú)

        [TestMethod]
        public void LuceneBuscador_PalabraExacta_SaleAntesQueLosQueSoloCasanPorElRadical()
        {
            // El stemmer deja "vapore" en "vapor", así que "vapore" era la misma búsqueda que
            // "vapor": 65 productos y la Vapore la 26ª, detrás de vasos y gomas de vapor con el
            // nombre corto y sin descripción. La Vapore tiene descripción larga, que diluye su
            // puntuación en el texto completo.
            ResultadoBusqueda vapore = CrearProducto("37668", "VAPORE CREMA SUSTITUTIVA DEL VAPOR", anulado: false);
            vapore.DescripcionLarga = "Crema de la línea Splendore que sustituye al vapor en la cabina para ablandar el poro "
                + "antes de la extracción. Se aplica una capa generosa sobre el rostro limpio y se deja actuar diez minutos "
                + "cubriendo con film o toalla templada. Ideal para pieles sensibles que no toleran el calor del aparato.";
            Indexar(
                CrearProducto("32841", "VASO VAPOR SILVERFOX (Vapor B-002)", anulado: false),
                CrearProducto("33872", "VASO VAPOR (Vapor A-30 / FD-2103)", anulado: false),
                CrearProducto("22703", "VASO VAPOR", anulado: false),
                CrearProducto("27166", "GOMA VASO VAPOR", anulado: false),
                CrearProducto("27163", "RESISTENCIA VAPOR", anulado: false),
                vapore);

            List<string> porVapore = Ids(Buscar("vapore", incluirAnulados: false));
            List<string> porVapor = Ids(Buscar("vapor", incluirAnulados: false));

            Assert.AreEqual(6, porVapore.Count, "el radical sigue encontrando a los de vapor: no se pierde nada");
            Assert.AreEqual("37668", porVapore[0], "la palabra tal cual manda sobre el radical");
            Assert.AreEqual(6, porVapor.Count, "\"vapor\" sigue encontrando la Vapore");
        }

        [TestMethod]
        public void LuceneBuscador_PalabraExacta_IgnoraAcentosYMayusculas()
        {
            // (El stemmer ligero no junta "champús" con "champú", pero sí "cremas" con "crema".)
            Indexar(
                CrearProducto("plural", "Cremas para la casa", anulado: false),
                CrearProducto("exacto", "Crema hidratante", anulado: false),
                CrearProducto("acento", "Champú anticaspa", anulado: false));

            List<string> porCrema = Ids(Buscar("CREMA", incluirAnulados: false));
            List<string> porChampu = Ids(Buscar("champu", incluirAnulados: false));

            Assert.AreEqual(2, porCrema.Count, "el radical sigue encontrando el plural");
            Assert.AreEqual("exacto", porCrema[0], "y el exacto, en mayúsculas, pone delante al que se llama así");
            CollectionAssert.AreEqual(new List<string> { "acento" }, porChampu, "sin acento también casa");
        }

        #endregion

        [TestMethod]
        public void LuceneBuscador_LosVideosNoDegradanElNombreDeLosProductos()
        {
            // 03/09/26: en producción "vapor" daba la Vapore la 26ª y en local, con los mismos datos,
            // la primera. La diferencia eran los vídeos: su título iba como StringField("Nombre")
            // (OmitNorms + DOCS_ONLY), y en Lucene 4 eso se contagia al campo entero del segmento:
            // el Nombre de TODOS los productos perdía boost, normalización y frecuencia.
            ResultadoBusqueda vapore = CrearProducto("37668", "VAPORE CREMA SUSTITUTIVA DEL VAPOR", anulado: false);
            vapore.DescripcionLarga = "Crema de la línea Splendore que sustituye al vapor en la cabina para ablandar el poro "
                + "antes de la extracción. Se aplica una capa generosa sobre el rostro limpio y se deja actuar diez minutos "
                + "cubriendo con film o toalla templada. Ideal para pieles sensibles que no toleran el calor del aparato.";
            List<ResultadoBusqueda> productos = new List<ResultadoBusqueda>
            {
                CrearProducto("32841", "VASO VAPOR SILVERFOX (Vapor B-002)", anulado: false),
                CrearProducto("33872", "VASO VAPOR (Vapor A-30 / FD-2103)", anulado: false),
                vapore
            };
            (int, string, string, string) video = (99, "Protocolo de manicura", "transcripción", "Vídeo de manicura");

            LuceneBuscador.Indexar(_rutaIndice, productos, new List<(int, string, string, string)>());
            List<string> sinVideo = Ids(LuceneBuscador.BuscarEnIndice(_rutaIndice, new ParametrosBusqueda { Query = "vapor", Tipo = "producto" }));
            LuceneBuscador.Indexar(_rutaIndice, productos, new List<(int, string, string, string)> { video });
            List<string> conVideo = Ids(LuceneBuscador.BuscarEnIndice(_rutaIndice, new ParametrosBusqueda { Query = "vapor", Tipo = "producto" }));

            Assert.AreEqual("37668", sinVideo[0], "por nombre (boost 4), vapor dos veces en cuatro palabras gana a dos veces en seis");
            CollectionAssert.AreEqual(sinVideo, conVideo, "un vídeo en el índice no puede cambiar el orden de los productos");
        }

        #region Los más vendidos pesan (03/09/26: ClasificacionMasVendidos no se miraba)

        [TestMethod]
        public void LuceneBuscador_FactorMasVendido_ElPrimeroDoblaYElUltimoNoSuma()
        {
            Assert.AreEqual(1f + PESO_MAS_VENDIDOS, FactorMasVendido(1), 0.0001f);
            Assert.AreEqual(1f, FactorMasVendido(0), "sin clasificar (vídeos, productos nuevos) se queda igual");
            Assert.AreEqual(1f, FactorMasVendido(-1));
            Assert.AreEqual(1f, FactorMasVendido(1000000), "más allá del horizonte no suma");
            Assert.IsTrue(FactorMasVendido(10) > FactorMasVendido(100), "decrece con la posición");
            Assert.IsTrue(FactorMasVendido(100) > FactorMasVendido(10000));
            Assert.IsTrue(FactorMasVendido(36000) > 1f, "el último de hoy aún suma algo");
        }

        [TestMethod]
        public void LuceneBuscador_MasVendido_AdelantaAIgualRelevancia()
        {
            // Los tres se llaman igual: por texto empatan y Lucene los daría en orden de indexado
            // (el sin clasificar el primero). Lo que se vende manda.
            Indexar(
                CrearProducto("sin", "Champú anticaspa", anulado: false, posicionMasVendido: null),
                CrearProducto("medio", "Champú anticaspa", anulado: false, posicionMasVendido: 5000),
                CrearProducto("top", "Champú anticaspa", anulado: false, posicionMasVendido: 10));

            List<string> resultados = Ids(Buscar("champú", incluirAnulados: false));

            CollectionAssert.AreEqual(new List<string> { "top", "medio", "sin" }, resultados);
        }

        [TestMethod]
        public void LuceneBuscador_MasVendido_NoAdelantaAUnaCoincidenciaClaramenteMejor()
        {
            // El peso multiplica la puntuación de texto, no la sustituye: el que casa con las dos
            // palabras sigue por delante del superventas que solo casa con una.
            Indexar(
                CrearProducto("superventas", "Champú", anulado: false, posicionMasVendido: 1),
                CrearProducto("exacto", "Champú anticaspa", anulado: false, posicionMasVendido: 30000));

            List<string> resultados = Ids(Buscar("champú anticaspa", incluirAnulados: false));

            Assert.AreEqual(2, resultados.Count);
            Assert.AreEqual("exacto", resultados[0]);
        }

        [TestMethod]
        public void LuceneBuscador_MasVendido_NoApareceEnBusquedasQueNoLeTocan()
        {
            Indexar(
                CrearProducto("superventas", "Cartucho cera tibia natural", anulado: false, posicionMasVendido: 1),
                CrearProducto("normal", "Champú anticaspa", anulado: false, posicionMasVendido: 20000));

            List<string> resultados = Ids(Buscar("champú", incluirAnulados: false));

            CollectionAssert.AreEqual(new List<string> { "normal" }, resultados);
        }

        [TestMethod]
        public void LuceneBuscador_MasVendido_UnIndiceSoloDeVideosNoRevienta()
        {
            // Los vídeos no llevan posición: en un segmento sin el campo, los doc values son null
            LuceneBuscador.Indexar(
                _rutaIndice,
                new List<ResultadoBusqueda>(),
                new List<(int, string, string, string)> { (99, "Protocolo de manicura", "transcripción", "Vídeo de manicura") });

            List<dynamic> resultados = Buscar("manicura", incluirAnulados: false);

            Assert.AreEqual(1, resultados.Count);
            Assert.AreEqual("video", (string)resultados[0].Tipo);
        }

        #endregion

        #region Rescate difuso: una errata no puede dejar la búsqueda a cero

        [TestMethod]
        public void LuceneBuscador_ConErrata_RescataElProducto()
        {
            // 03/09/26: "richeza" devolvía 0 resultados y "ricchezza" 13.
            Indexar(
                CrearProducto("35894", "ACIDO HIALURONICO RICCHEZZA", anulado: false),
                CrearProducto("22535", "CERA GOLD", anulado: false));

            List<string> conErrata = Ids(Buscar("richeza", incluirAnulados: false));
            List<string> bienEscrito = Ids(Buscar("ricchezza", incluirAnulados: false));

            CollectionAssert.AreEqual(new List<string> { "35894" }, conErrata, "la errata se rescata");
            CollectionAssert.AreEqual(new List<string> { "35894" }, bienEscrito, "y escribiendo bien sigue igual");
        }

        [TestMethod]
        public void LuceneBuscador_SiLaBusquedaNormalEncuentraAlgo_NoSeMezclaElDifuso()
        {
            // El rescate solo entra cuando no hay NADA: quien escribe bien no puede ver resultados
            // peores por culpa del difuso.
            Indexar(
                CrearProducto("1", "CERA GOLD", anulado: false),
                CrearProducto("2", "VERA NATURAL", anulado: false),
                CrearProducto("3", "SERA FACIAL", anulado: false));

            List<string> resultados = Ids(Buscar("cera", incluirAnulados: false));

            CollectionAssert.AreEqual(new List<string> { "1" }, resultados);
        }

        [TestMethod]
        public void LuceneBuscador_ErrataEnUnaReferencia_NoRescata()
        {
            // Un número no es una errata: buscar el 17404 no puede devolver el 17405
            Indexar(
                CrearProducto("17404", "Cera gold", anulado: false),
                CrearProducto("17405", "Cera de abeja", anulado: false));

            Assert.AreEqual(0, Buscar("17406", incluirAnulados: false).Count);
        }

        [TestMethod]
        public void LuceneBuscador_ConsultaDifusa_MarcaSoloLasPalabrasLargas()
        {
            Assert.AreEqual("richeza~", LuceneBuscador.ConsultaDifusa("richeza"));
            Assert.AreEqual("crema~ de la noche~", LuceneBuscador.ConsultaDifusa("crema de la noche"),
                "las palabras de menos de 4 letras se quedan como están");
            Assert.AreEqual("17404", LuceneBuscador.ConsultaDifusa("17404"), "una referencia no admite erratas");
            Assert.AreEqual("cera~ 17404", LuceneBuscador.ConsultaDifusa("cera 17404"));
        }

        [TestMethod]
        public void LuceneBuscador_HayPalabraParaDifusa_SoloConPalabrasLargas()
        {
            Assert.IsTrue(LuceneBuscador.HayPalabraParaDifusa("richeza"));
            Assert.IsFalse(LuceneBuscador.HayPalabraParaDifusa("17404"), "una referencia no dispara el rescate");
            Assert.IsFalse(LuceneBuscador.HayPalabraParaDifusa("de la"));
            Assert.IsFalse(LuceneBuscador.HayPalabraParaDifusa(""));
            Assert.IsFalse(LuceneBuscador.HayPalabraParaDifusa(null));
        }

        #endregion

        #region Rescate fonético: escrito como suena ("rikeza" -> "ricchezza")

        [TestMethod]
        public void LuceneBuscador_EscritoComoSuena_RescataElProducto()
        {
            // "rikeza" está a tres letras de "ricchezza": fuera del alcance del difuso (máximo dos),
            // pero suena igual.
            Indexar(
                CrearProducto("35894", "ACIDO HIALURONICO RICCHEZZA", anulado: false),
                CrearProducto("22535", "CERA GOLD", anulado: false));

            List<string> resultados = Ids(Buscar("rikeza", incluirAnulados: false));

            CollectionAssert.AreEqual(new List<string> { "35894" }, resultados);
        }

        [TestMethod]
        public void LuceneBuscador_EscritoComoSuena_LasConfusionesTipicas()
        {
            Indexar(
                CrearProducto("1", "GEL LIMPIADOR SPLENDORE", anulado: false),
                CrearProducto("2", "MASCARILLA HIALURONICO", anulado: false),
                CrearProducto("3", "QUERATINA LIQUIDA", anulado: false));

            CollectionAssert.AreEqual(new List<string> { "1" }, Ids(Buscar("jel esplendore", incluirAnulados: false)), "g/j y la e- inicial");
            CollectionAssert.AreEqual(new List<string> { "2" }, Ids(Buscar("mascariya ialuronico", incluirAnulados: false)), "ll/y y la hache muda");
            CollectionAssert.AreEqual(new List<string> { "3" }, Ids(Buscar("keratina", incluirAnulados: false)), "k/qu");
        }

        [TestMethod]
        public void LuceneBuscador_LoFoneticoSoloEntraEnElRescate()
        {
            // "cera" y "sera" suenan igual, pero mientras "cera" encuentre algo no se mezclan
            Indexar(
                CrearProducto("1", "CERA GOLD", anulado: false),
                CrearProducto("2", "SERA FACIAL", anulado: false));

            CollectionAssert.AreEqual(new List<string> { "1" }, Ids(Buscar("cera", incluirAnulados: false)));
        }

        [TestMethod]
        public void ClaveFoneticaEspanola_LasReglasDelEspanol()
        {
            Assert.AreEqual(ClaveFoneticaEspanola.Calcular("ricchezza"), ClaveFoneticaEspanola.Calcular("rikeza"));
            Assert.AreEqual(ClaveFoneticaEspanola.Calcular("bomba"), ClaveFoneticaEspanola.Calcular("vomba"), "b/v");
            Assert.AreEqual(ClaveFoneticaEspanola.Calcular("cera"), ClaveFoneticaEspanola.Calcular("sera"), "seseo");
            Assert.AreEqual(ClaveFoneticaEspanola.Calcular("gel"), ClaveFoneticaEspanola.Calcular("jel"), "g/j");
            Assert.AreEqual(ClaveFoneticaEspanola.Calcular("mascarilla"), ClaveFoneticaEspanola.Calcular("mascariya"), "yeísmo");
            Assert.AreEqual(ClaveFoneticaEspanola.Calcular("hialuronico"), ClaveFoneticaEspanola.Calcular("ialuronico"), "hache muda");
            Assert.AreEqual(ClaveFoneticaEspanola.Calcular("splendore"), ClaveFoneticaEspanola.Calcular("esplendore"), "e- inicial");
            Assert.AreEqual(ClaveFoneticaEspanola.Calcular("keratina"), ClaveFoneticaEspanola.Calcular("queratina"), "k/qu");

            Assert.AreNotEqual(ClaveFoneticaEspanola.Calcular("cera"), ClaveFoneticaEspanola.Calcular("vera"), "productos distintos siguen distintos");
            Assert.AreNotEqual(ClaveFoneticaEspanola.Calcular("cera"), ClaveFoneticaEspanola.Calcular("cara"));
            Assert.AreEqual(string.Empty, ClaveFoneticaEspanola.Calcular(null));
            Assert.AreEqual(string.Empty, ClaveFoneticaEspanola.Calcular("   "));
        }

        #endregion

        #region Buscar por referencia (la caja del footer de la tienda, Nesto y la app)

        [TestMethod]
        public void LuceneBuscador_BuscarPorReferencia_EncuentraElProducto()
        {
            // 02/09/26: "17404" no devolvía nada. La referencia solo se guardaba para devolverla
            // (StringField Id) y la consulta iba contra TextoCompleto/Nombre/Protocolo.
            Indexar(
                CrearProducto("17404", "Cera gold", anulado: false),
                CrearProducto("22535", "Cera de abeja", anulado: false));

            List<dynamic> resultados = Buscar("17404", incluirAnulados: false);

            Assert.AreEqual(1, resultados.Count);
            Assert.AreEqual("17404", (string)resultados[0].Id);
        }

        [TestMethod]
        public void LuceneBuscador_BuscarPorReferencia_ElProductoConEsaReferenciaSaleElPrimero()
        {
            ResultadoBusqueda recambio = CrearProducto("30001", "Recambio cabezal", anulado: false);
            recambio.DescripcionLarga = "Recambio para el aparato 17404 y para el 17405. Compatible con 17404.";
            Indexar(
                recambio,
                CrearProducto("17404", "Aparato de presoterapia", anulado: false));

            List<dynamic> resultados = Buscar("17404", incluirAnulados: false);

            Assert.AreEqual(2, resultados.Count, "el recambio la menciona en la descripción y también sale");
            Assert.AreEqual("17404", (string)resultados[0].Id, "la referencia exacta manda sobre las menciones");
        }

        [TestMethod]
        public void LuceneBuscador_BuscarPorReferenciaYTexto_SigueEncontrando()
        {
            Indexar(
                CrearProducto("17404", "Cera gold", anulado: false),
                CrearProducto("22535", "Cera de abeja", anulado: false));

            List<dynamic> porTexto = Buscar("cera", incluirAnulados: false);
            List<dynamic> mezcla = Buscar("cera 17404", incluirAnulados: false);

            Assert.AreEqual(2, porTexto.Count);
            Assert.AreEqual("17404", (string)mezcla[0].Id);
        }

        [TestMethod]
        public void LuceneBuscador_BuscarPorReferencia_NoEncuentraLoQueNoExiste()
        {
            Indexar(CrearProducto("17404", "Cera gold", anulado: false));

            Assert.AreEqual(0, Buscar("99999", incluirAnulados: false).Count);
        }

        #endregion

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
