using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Videos;
using System.Linq;

namespace NestoAPI.Tests.Infraestructure.Videos
{
    /// <summary>
    /// Equipo de SEO, 08/09/26: 34 de los 778 vídeos no tienen Descripcion en Nesto y la ficha de la
    /// tienda se quedaba en la frase del título. 14 de ellos SÍ tienen protocolo; estos tests fijan
    /// cómo se aprovecha, y sobre todo qué NO se publica.
    ///
    /// <para>Los casos feos están sacados de los protocolos REALES de producción, no inventados: el
    /// mensaje de error de OpenAI del 1830, el corchete vacío del 1849, los dos puntos de cabecera
    /// del 1660 y la frase empezada por la mitad del 1330.</para>
    /// </summary>
    [TestClass]
    public class DescripcionFichaVideoTests
    {
        /// <summary>Cabecera con la que NVIA abre TODOS los protocolos, literal de producción.</summary>
        private const string CabeceraDeTodosLosProtocolos =
            "<h1>Protocolo Profesional de Tratamiento Estético</h1><h2>Introducción</h2>";

        private static string ParrafoLargo(string arranque)
        {
            return arranque + " " + string.Join(" ", Enumerable.Repeat("relleno", 20));
        }

        [TestMethod]
        public void DescripcionFichaVideo_SiHayDescripcion_LaDevuelveTalCual()
        {
            string descripcion = "Cómo se aplica el masaje facial Kobido paso a paso, con los tiempos de cada maniobra.";

            string resultado = DescripcionFichaVideo.Componer(descripcion, "<p>" + ParrafoLargo("Protocolo.") + "</p>");

            Assert.AreEqual(descripcion, resultado);
        }

        [TestMethod]
        public void DescripcionFichaVideo_SiLaDescripcionEstaEnBlanco_CaeAlProtocolo()
        {
            string protocolo = CabeceraDeTodosLosProtocolos
                + "<p>En este protocolo abordamos el tratamiento facial a partir de los vectores faciales, "
                + "que definen la dirección de la tensión de la piel en cada maniobra.</p>";

            string resultado = DescripcionFichaVideo.Componer("   ", protocolo);

            StringAssert.StartsWith(resultado, "En este protocolo abordamos");
        }

        [TestMethod]
        public void DescripcionFichaVideo_SiNoHayNiDescripcionNiProtocolo_DevuelveNull()
        {
            Assert.IsNull(DescripcionFichaVideo.Componer(null, null));
            Assert.IsNull(DescripcionFichaVideo.Componer("", ""));
            Assert.IsNull(DescripcionFichaVideo.Componer("  ", "   "));
        }

        [TestMethod]
        public void DescripcionFichaVideo_NuncaEmpiezaPorLaCabeceraComunATodosLosProtocolos()
        {
            // Es el fallo que hay que evitar: si se colara, los 778 vídeos tendrían la misma
            // primera frase y Google los leería como fichas clonadas.
            string protocolo = CabeceraDeTodosLosProtocolos
                + "<p>Comenzamos con una evaluación exhaustiva de la piel para identificar el fototipo "
                + "del cliente, que condiciona todo el tratamiento posterior.</p>";

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(protocolo);

            Assert.IsFalse(resultado.Contains("Protocolo Profesional de Tratamiento Estético"));
            Assert.IsFalse(resultado.Contains("Introducción"));
            StringAssert.StartsWith(resultado, "Comenzamos con una evaluación");
        }

        [TestMethod]
        public void DescripcionFichaVideo_ElCaso1667_YaNoSePublicaConLaOpcionC()
        {
            // Este texto SÍ se publicaba antes del 08/09/26 y se lee perfectamente. Cuelga de
            // "1. Diagnóstico Inicial", así que la opción C se lo lleva. Es el precio consciente de
            // tener una regla objetiva: el 1667 y el 1847 pasan a escribirse a mano.
            string protocolo = "<h1>Protocolo Profesional de Tratamiento Estético</h1>"
                + "<h2>1. Diagnóstico Inicial</h2><ul><li>Comenzamos el tratamiento con una evaluación "
                + "exhaustiva de la piel, concentrando los esfuerzos en identificar el fototipo.</li></ul>";

            Assert.IsNull(DescripcionFichaVideo.ExtractoDeProtocolo(protocolo));
        }

        [TestMethod]
        public void DescripcionFichaVideo_QuitaLosEnlacesVerPasoEnVideo()
        {
            string protocolo = "<h2>Preparación de la piel</h2><ul><li>Preparamos la piel asegurándonos de que esté limpia y "
                + "libre de impurezas, con un limpiador suave que respete su equilibrio natural. "
                + "<a href=\"https://www.youtube.com/watch?v=Pub2XYntU7k&t=11s\">Ver paso en video</a></li></ul>";

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(protocolo);

            Assert.IsFalse(resultado.Contains("Ver paso en video"));
            Assert.IsFalse(resultado.Contains("youtube.com"));
        }

        [TestMethod]
        public void DescripcionFichaVideo_NoDejaElCorcheteVacioDelEnlaceQuitado()
        {
            // Vídeo 1849 (Cap 20: Electroestetica 2): el protocolo real acaba en "[ ]" porque el
            // enlace iba dentro de un corchete.
            string protocolo = "<p>Comenzamos el tratamiento con una introducción a las corrientes continuas. "
                + "Se destaca su importancia en la estética debido a la dirección constante de los electrones. "
                + "[<a href=\"https://youtu.be/x\">ver</a>]</p>";

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(protocolo);

            Assert.IsFalse(resultado.Contains("["), "Queda un corchete: " + resultado);
            Assert.IsFalse(resultado.Contains("]"), "Queda un corchete: " + resultado);
            Assert.IsTrue(resultado.TrimEnd().EndsWith("electrones."), "Debe acabar en la frase: " + resultado);
        }

        [TestMethod]
        public void DescripcionFichaVideo_NoDejaLaPuntuacionSeparadaDeSuPalabra()
        {
            // Vídeo 1822 (Cap 5: Manto Hidrolipídico): el protocolo real acababa en " ." al quitar
            // la etiqueta que había antes del punto.
            string protocolo = "<p>El manto hidrolipídico es una emulsión epicutánea que actúa como la primera "
                + "barrera protectora de la piel, compuesta por lípidos y agua<b></b> .</p>";

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(protocolo);

            Assert.IsFalse(resultado.Contains(" ."), "Punto suelto: " + resultado);
        }

        [TestMethod]
        public void DescripcionFichaVideo_NoDuplicaElPuntoFinal_PeroRespetaLosPuntosSuspensivos()
        {
            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(
                "<p>" + ParrafoLargo("El manto hidrolipídico protege la piel.") + "<b></b> .</p>");

            Assert.IsFalse(resultado.Contains(".."), "Punto duplicado: " + resultado);

            string conSuspensivos = DescripcionFichaVideo.ExtractoDeProtocolo(
                "<p>" + ParrafoLargo("Mesoterapia, ultrasonido... y alguna técnica más.") + "</p>");

            StringAssert.Contains(conSuspensivos, "ultrasonido...");
        }

        [TestMethod]
        public void DescripcionFichaVideo_NoPublicaElMensajeDeErrorDeOpenAI()
        {
            // Vídeo 1830 (Cap. 8: Foliculitis): su Protocolo en producción es exactamente esto.
            Assert.IsNull(DescripcionFichaVideo.ExtractoDeProtocolo("Error en la creación del protocolo"));
            Assert.IsNull(DescripcionFichaVideo.ExtractoDeProtocolo("<p>[SIN CAPTIONS DISPONIBLES]</p>"));
        }

        [TestMethod]
        public void DescripcionFichaVideo_NoEmpiezaUnaFraseAMedias()
        {
            // Vídeo 1330 (Taller de Maquillaje de Festival): el primer trozo largo del protocolo
            // real empieza en minúscula ("retirando con agua cualquier resto..."), que en la ficha
            // se lee como si faltara el principio.
            string protocolo = "<ul><li>" + ParrafoLargo("retirando con agua cualquier resto de productos previos.") + "</li>"
                + "<li>" + ParrafoLargo("Aplicamos la base de maquillaje en capas finas.") + "</li></ul>";

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(protocolo);

            StringAssert.StartsWith(resultado, "Aplicamos la base de maquillaje");
        }

        [TestMethod]
        public void DescripcionFichaVideo_QuitaLosDosPuntosDeCabeceraDelArranque()
        {
            // Vídeo 1660 (Mesoterapia, ultrasonido... ¿diferencias?): el trozo real empieza por ":".
            string protocolo = "<p>: " + ParrafoLargo("Este es un término que se refiere al lanzamiento de sustancias al mesodermo.") + "</p>";

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(protocolo);

            StringAssert.StartsWith(resultado, "Este es un término");
        }

        [TestMethod]
        public void DescripcionFichaVideo_NoPublicaLoQueCuelgaDeUnPasoNumerado()
        {
            // Opción C del equipo de SEO (08/09/26). El caso que la decidió es el vídeo 1468, cuyo
            // primer párrafo cuelga de "Paso 1: Presentación del producto" y dice "un producto de
            // la marca Eva bisn": Eva Visnú mal transcrita. Una marca del catálogo mal escrita en
            // el texto que enseña Google es peor que no tener descripción.
            string protocolo = "<h1>Protocolo Profesional de Tratamiento Estético</h1>"
                + "<h2>Paso 1: Presentación del producto</h2><ul><li>"
                + ParrafoLargo("En este tratamiento se va a utilizar un producto de la marca Eva bisn.")
                + "</li></ul>";

            Assert.IsNull(DescripcionFichaVideo.ExtractoDeProtocolo(protocolo));
        }

        [TestMethod]
        public void DescripcionFichaVideo_ReconoceLasVariantesDeEncabezadoDePaso()
        {
            foreach (string encabezado in new[] { "Paso 1", "Paso 2: Comenzamos con la base", "paso 3",
                                                  "1. Diagnóstico Inicial", "2)", "3 -" })
            {
                string protocolo = "<h2>" + encabezado + "</h2><p>" + ParrafoLargo("Aplicamos el producto.") + "</p>";

                Assert.IsNull(DescripcionFichaVideo.ExtractoDeProtocolo(protocolo),
                    "Debería descartarse lo que cuelga de: " + encabezado);
            }
        }

        [TestMethod]
        public void DescripcionFichaVideo_UnEncabezadoQueNoEsUnPasoSiDejaPasarElTexto()
        {
            // La regla tiene que ser estrecha: estos encabezados reales de producción NO son pasos
            // y sus párrafos son justo los que se quieren publicar.
            foreach (string encabezado in new[] { "Introducción", "Definición y Comprensión:",
                                                  "Diagnóstico Inicial:", "Descripción Inicial:",
                                                  "La Mesoterapia Virtual y El Ultrasonido" })
            {
                string protocolo = "<h2>" + encabezado + "</h2><p>"
                    + ParrafoLargo("El manto hidrolipídico protege la piel.") + "</p>";

                StringAssert.StartsWith(DescripcionFichaVideo.ExtractoDeProtocolo(protocolo),
                    "El manto hidrolipídico", "No debería descartarse lo que cuelga de: " + encabezado);
            }
        }

        [TestMethod]
        public void DescripcionFichaVideo_SiElProtocoloSoloTieneEncabezados_DevuelveNull()
        {
            // Mejor que la tienda se quede con su texto de reserva que publicar un encabezado suelto.
            Assert.IsNull(DescripcionFichaVideo.ExtractoDeProtocolo("<h1>Protocolo</h1><h2>Foliculitis</h2>"));
        }

        [TestMethod]
        public void DescripcionFichaVideo_NoPublicaElProtocoloEntero_SoloUnArranque()
        {
            // El protocolo es lo que se le da al cliente: de una página pública sale un extracto.
            string parrafoLargo = "<p>Diagnosticamos la piel " + string.Join(" ", Enumerable.Repeat("palabra", 120)) + "</p>";
            string protocolo = CabeceraDeTodosLosProtocolos + parrafoLargo + "<h2>Paso 2</h2><p>Secreto.</p>";

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(protocolo);

            Assert.IsTrue(resultado.Length <= DescripcionFichaVideo.LongitudMaximaExtracto + 1,
                $"El extracto mide {resultado.Length}, y el tope son {DescripcionFichaVideo.LongitudMaximaExtracto}");
            Assert.IsFalse(resultado.Contains("Secreto"), "No debe llegar al resto del protocolo");
        }

        [TestMethod]
        public void DescripcionFichaVideo_RecortaPorPalabraEntera()
        {
            string frase = "Diagnosticamos " + string.Join(" ", Enumerable.Repeat("abcdefghij", 80));

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo("<p>" + frase + "</p>");

            Assert.IsTrue(resultado.EndsWith("…"), "Debe rematar en puntos suspensivos: " + resultado);
            Assert.IsFalse(resultado.Replace("…", "").TrimEnd().EndsWith("abcde"),
                "No debe cortar una palabra por la mitad: " + resultado);
        }

        [TestMethod]
        public void DescripcionFichaVideo_DescodificaLasEntidadesHtml()
        {
            string protocolo = "<p>La sesi&oacute;n dura 60 minutos y combina limpieza, exfoliaci&oacute;n "
                + "y masaje &amp; drenaje, seg&uacute;n el diagn&oacute;stico previo de la piel.</p>";

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(protocolo);

            StringAssert.Contains(resultado, "sesión");
            StringAssert.Contains(resultado, "masaje & drenaje");
            Assert.IsFalse(resultado.Contains("&oacute;"));
        }

        [TestMethod]
        public void DescripcionFichaVideo_ElTextoPlanoSinEtiquetasTambienVale()
        {
            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(
                "Aplicación del ácido mandélico al 30 % sobre piel grasa con tendencia acneica, "
                + "con los tiempos de exposición de cada pasada.");

            StringAssert.StartsWith(resultado, "Aplicación del ácido mandélico");
        }

        [TestMethod]
        public void DescripcionFichaVideo_NoPegaDosBloquesSinSeparacion()
        {
            string protocolo = "<p>" + ParrafoLargo("Uno.") + "</p><p>" + ParrafoLargo("Dos.") + "</p>";

            string resultado = DescripcionFichaVideo.ExtractoDeProtocolo(protocolo);

            Assert.IsFalse(resultado.Contains("rellenoDos"), "Las etiquetas deben separar, no desaparecer");
        }
    }
}
