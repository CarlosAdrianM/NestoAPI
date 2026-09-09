using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>
    /// NestoAPI#458: los 150 € por efecto, compartidos por el selector de plazos y por
    /// CondicionesPago (tienda y app). Un cliente con «30/60/90» en ficha no lo recibe para un
    /// pedido de 200 €, sí para uno de 450 €, y sin totalPedido lo sigue recibiendo.
    /// </summary>
    [TestClass]
    public class PoliticaImporteMinimoEfectoTests
    {
        private static PlazoPagoDTO Plazo(string codigo, short numeroPlazos, short diasPrimerPlazo, short mesesPrimerPlazo = 0)
        {
            return new PlazoPagoDTO { plazoPago = codigo, numeroPlazos = numeroPlazos, diasPrimerPlazo = diasPrimerPlazo, mesesPrimerPlazo = mesesPrimerPlazo };
        }

        private static List<PlazoPagoDTO> Catalogo()
        {
            return new List<PlazoPagoDTO>
            {
                Plazo(Constantes.PlazosPago.PREPAGO, 1, 0),
                Plazo("CONTADO", 1, 0),
                Plazo("30", 1, 30),
                Plazo("30/60/90", 3, 30),
                Plazo("ENT+2", 3, 0) // entrada y dos efectos
            };
        }

        private static List<string> Codigos(IEnumerable<PlazoPagoDTO> plazos)
        {
            return plazos.Select(p => p.plazoPago).ToList();
        }

        [TestMethod]
        public void Filtrar_Pedido200_NoOfrece306090()
        {
            List<PlazoPagoDTO> resultado = PoliticaImporteMinimoEfecto.Filtrar(Catalogo(), 200, null);

            CollectionAssert.DoesNotContain(Codigos(resultado), "30/60/90", "200/3 = 66 € por efecto, por debajo de 150");
            CollectionAssert.DoesNotContain(Codigos(resultado), "ENT+2", "200/2 = 100 € por efecto");
            CollectionAssert.Contains(Codigos(resultado), "30", "un solo efecto de 200 € sí llega");
            CollectionAssert.Contains(Codigos(resultado), "CONTADO");
            CollectionAssert.Contains(Codigos(resultado), Constantes.PlazosPago.PREPAGO);
        }

        [TestMethod]
        public void Filtrar_Pedido450_SiOfrece306090()
        {
            List<PlazoPagoDTO> resultado = PoliticaImporteMinimoEfecto.Filtrar(Catalogo(), 450, null);

            CollectionAssert.Contains(Codigos(resultado), "30/60/90", "450/3 = 150 € justos");
            CollectionAssert.Contains(Codigos(resultado), "ENT+2", "450/2 = 225 €");
        }

        [TestMethod]
        public void Filtrar_LaEntradaNoCuentaComoEfecto()
        {
            // 300 € con entrada y dos efectos: 300/2 = 150 pasa; 30/60/90 sin entrada: 300/3 = 100 no pasa.
            List<string> resultado = Codigos(PoliticaImporteMinimoEfecto.Filtrar(Catalogo(), 300, null));

            CollectionAssert.Contains(resultado, "ENT+2");
            CollectionAssert.DoesNotContain(resultado, "30/60/90");
        }

        [TestMethod]
        public void Filtrar_ElPlazoAutorizadoEnLaFichaSeOfreceAunqueNoLlegueAlMinimo()
        {
            // El ImporteMínimo de la ficha es un umbral: autorizado a partir de él.
            var ficha = new[] { new PoliticaImporteMinimoEfecto.CondicionFicha { PlazosPago = "30/60/90 ", ImporteMinimo = 100 } };

            List<string> con200 = Codigos(PoliticaImporteMinimoEfecto.Filtrar(Catalogo(), 200, ficha));
            List<string> con50 = Codigos(PoliticaImporteMinimoEfecto.Filtrar(Catalogo(), 50, ficha));

            CollectionAssert.Contains(con200, "30/60/90", "200 >= 100: la ficha lo autoriza (y el código viene con relleno)");
            CollectionAssert.DoesNotContain(con50, "30/60/90", "50 < 100: la ficha no lo autoriza para ese importe");
        }

        [TestMethod]
        public void Filtrar_ImporteCeroONegativo_SoloValenLasCondicionesDeImporteMinimoCero()
        {
            var ficha = new[]
            {
                new PoliticaImporteMinimoEfecto.CondicionFicha { PlazosPago = "30/60/90", ImporteMinimo = 0 },
                new PoliticaImporteMinimoEfecto.CondicionFicha { PlazosPago = "ENT+2", ImporteMinimo = 100 }
            };

            List<string> resultado = Codigos(PoliticaImporteMinimoEfecto.Filtrar(Catalogo(), -20, ficha));

            CollectionAssert.Contains(resultado, "30/60/90");
            CollectionAssert.DoesNotContain(resultado, "ENT+2");
        }

        [TestMethod]
        public void Filtrar_PlazoConNumeroDePlazosCero_NoRevientaNiSeOfrece()
        {
            // Antes: totalPedido / (0 - 1)... o división por cero; el catch devolvía la lista SIN
            // filtrar. Ahora un plazo raro simplemente no pasa, y no hay catch.
            List<PlazoPagoDTO> raro = new List<PlazoPagoDTO> { Plazo("RARO", 0, 30), Plazo("CONTADO", 1, 0) };

            List<string> resultado = Codigos(PoliticaImporteMinimoEfecto.Filtrar(raro, 200, null));

            CollectionAssert.Contains(resultado, "RARO", "numeroPlazos <= 1 se trata como contado");
            CollectionAssert.Contains(resultado, "CONTADO");
        }

        [TestMethod]
        public void AplicarSiHayImporte_SinTotalPedido_NoTocaNada()
        {
            CondicionesPagoResponse condiciones = new CondicionesPagoResponse { PlazosPago = Catalogo() };

            CondicionesPagoResponse resultado = PoliticaImporteMinimoEfecto.AplicarSiHayImporte(condiciones, null, null);

            Assert.AreEqual(5, resultado.PlazosPago.Count, "compatibilidad: quien no manda importe sigue viendo lo de siempre");
        }

        [TestMethod]
        public void AplicarSiHayImporte_ConTotalPedido_FiltraLosPlazosDeLaRespuesta()
        {
            CondicionesPagoResponse condiciones = new CondicionesPagoResponse { PlazosPago = Catalogo() };

            CondicionesPagoResponse resultado = PoliticaImporteMinimoEfecto.AplicarSiHayImporte(condiciones, 200, null);

            CollectionAssert.DoesNotContain(Codigos(resultado.PlazosPago), "30/60/90");
            CollectionAssert.Contains(Codigos(resultado.PlazosPago), "30");
        }

        [TestMethod]
        public void AplicarSiHayImporte_DespuesDeLaPoliticaApp_SigueSiendoContadoMasFichaYCumpleImporte()
        {
            // Lo que ve la tienda: la política del canal deja contado + lo de la ficha; el importe
            // quita de ahí lo que no llega, salvo que la ficha lo autorice para ese importe.
            CondicionesPagoResponse generales = new CondicionesPagoResponse { PlazosPago = Catalogo(), FormasPago = new List<FormaPagoDTO>(), InfoDeuda = new InfoDeudaClienteDTO() };
            PoliticaPagoCanal.CondicionesFicha fichaCanal = new PoliticaPagoCanal.CondicionesFicha { PlazosPago = new List<string> { "30/60/90", "30" } };
            var fichaImporte = new[] { new PoliticaImporteMinimoEfecto.CondicionFicha { PlazosPago = "30/60/90", ImporteMinimo = 300 } };

            CondicionesPagoResponse app = PoliticaPagoCanal.AplicarPoliticaApp(generales, fichaCanal);
            CondicionesPagoResponse con200 = PoliticaImporteMinimoEfecto.AplicarSiHayImporte(app, 200, fichaImporte);

            List<string> codigos = Codigos(con200.PlazosPago);
            CollectionAssert.DoesNotContain(codigos, "30/60/90", "autorizado solo a partir de 300 €");
            CollectionAssert.Contains(codigos, "30");
            CollectionAssert.Contains(codigos, Constantes.PlazosPago.PREPAGO);
            CollectionAssert.DoesNotContain(codigos, "ENT+2", "la política de la app ya lo había quitado: no está en la ficha");
        }
    }
}
