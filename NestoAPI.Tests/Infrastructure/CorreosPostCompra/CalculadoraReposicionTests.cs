using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#532: criterio del recordatorio de reposición (ventas de todos los canales).
    /// </summary>
    [TestClass]
    public class CalculadoraReposicionTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 24);
        private static readonly ConsumiblesReposicion CONSUMIBLES = ConsumiblesReposicion.Leer(null);

        private Dictionary<string, ProductoReposicion> productos;
        private List<CompraDiaReposicion> compras;
        private List<LineaPendienteReposicion> pendientes;

        [TestInitialize]
        public void Setup()
        {
            productos = new Dictionary<string, ProductoReposicion>(StringComparer.OrdinalIgnoreCase)
            {
                ["CREMA500"] = Producto("CREMA500", "Crema hidratante 500 ml", "COS", "125", "Fama"),
                ["CREMA1000"] = Producto("CREMA1000", "Crema hidratante 1000 ml", "COS", "125", "Fama"),
                ["CREMAOTRA"] = Producto("CREMAOTRA", "Crema de otra marca", "COS", "125", "Lendan"),
                ["TINTE"] = Producto("TINTE", "Tinte 6.0", "PEL", "TIN", "Tinta"),
                ["APARATO"] = Producto("APARATO", "Radiofrecuencia", "APA", "001", "Fama"),
                ["MUESTRA"] = Producto("MUESTRA", "Muestra crema", "COS", "MMP", "Fama")
            };
            compras = new List<CompraDiaReposicion>();
            pendientes = new List<LineaPendienteReposicion>();
        }

        private static ProductoReposicion Producto(string id, string nombre, string grupo, string subgrupo, string familia,
            short estado = 0, bool ficticio = false)
            => new ProductoReposicion
            {
                Producto = id,
                Nombre = nombre,
                Grupo = grupo,
                SubGrupo = subgrupo,
                Familia = familia,
                Estado = estado,
                Ficticio = ficticio
            };

        /// <summary>Compras del cliente de un producto, cada una hace N días.</summary>
        private void Compras(string cliente, string producto, decimal importe, params int[] haceDias)
        {
            foreach (int dias in haceDias)
            {
                compras.Add(new CompraDiaReposicion
                {
                    Cliente = cliente + "  ",
                    Producto = producto + "     ",
                    Dia = HOY.AddDays(-dias),
                    BaseImponible = importe
                });
            }
        }

        private List<CandidatoReposicionDTO> Evaluar()
            => CalculadoraReposicion.EvaluarPares(compras, pendientes, productos, CONSUMIBLES, HOY);

        #region Intervalo

        [TestMethod]
        public void IntervaloMediano_ConMenosDeTresDiasDeCompra_EsNull()
        {
            Assert.IsNull(CalculadoraReposicion.IntervaloMediano(new[] { HOY, HOY.AddDays(-30) }));
        }

        [TestMethod]
        public void IntervaloMediano_VariasComprasElMismoDia_CuentanComoUna()
        {
            Assert.IsNull(CalculadoraReposicion.IntervaloMediano(new[] { HOY, HOY, HOY.AddDays(-30) }));
        }

        [TestMethod]
        public void IntervaloMediano_ImparDeIntervalos_EsElCentral()
        {
            // Intervalos 30, 30 y 100: la mediana (30) aguanta el pedido suelto de acopio mejor que la media
            DateTime[] dias = { HOY.AddDays(-160), HOY.AddDays(-130), HOY.AddDays(-100), HOY };
            Assert.AreEqual(30, CalculadoraReposicion.IntervaloMediano(dias));
        }

        [TestMethod]
        public void IntervaloMediano_ParDeIntervalos_EsLaMediaDeLosDosCentrales()
        {
            DateTime[] dias = { HOY.AddDays(-100), HOY.AddDays(-80), HOY.AddDays(-40) };
            Assert.AreEqual(30, CalculadoraReposicion.IntervaloMediano(dias)); // 20 y 40
        }

        [TestMethod]
        public void TocaReponer_EntreUnoCoVeinticincoYTresVecesElIntervalo()
        {
            Assert.IsFalse(CalculadoraReposicion.TocaReponer(40, 49));
            Assert.IsTrue(CalculadoraReposicion.TocaReponer(40, 50));
            Assert.IsTrue(CalculadoraReposicion.TocaReponer(40, 119));
            Assert.IsFalse(CalculadoraReposicion.TocaReponer(40, 120), "Con 3 veces el ritmo ya es un cliente que se ha ido");
        }

        #endregion

        #region Qué pares tocan

        [TestMethod]
        public void EvaluarPares_RitmoDe30DiasYLleva40SinComprar_SeAvisaria()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);

            CandidatoReposicionDTO candidato = Evaluar().Single();

            Assert.IsTrue(candidato.SeAvisaria);
            Assert.AreEqual("1001", candidato.Cliente);
            Assert.AreEqual("CREMA500", candidato.Producto);
            Assert.AreEqual(30, candidato.IntervaloDias);
            Assert.AreEqual(40, candidato.DiasDesdeUltimaCompra);
            Assert.AreEqual(3, candidato.NumeroCompras);
            Assert.AreEqual(50m, candidato.ImporteHabitual);
            Assert.AreEqual(HOY.AddDays(-40), candidato.UltimaCompra);
        }

        [TestMethod]
        public void EvaluarPares_TodaviaNoLeToca_NoAparece()
        {
            Compras("1001", "CREMA500", 50m, 90, 60, 30);
            Assert.AreEqual(0, Evaluar().Count);
        }

        [TestMethod]
        public void EvaluarPares_ClienteQueSeHaIdo_NoAparece()
        {
            Compras("1001", "CREMA500", 50m, 150, 120, 90);
            Assert.AreEqual(0, Evaluar().Count, "90 días con ritmo de 30 = 3 veces: eso es otro correo (recuperar clientes)");
        }

        [TestMethod]
        public void EvaluarPares_SoloDosCompras_NoHayRitmoPropio()
        {
            Compras("1001", "CREMA500", 50m, 70, 40);
            Assert.AreEqual(0, Evaluar().Count);
        }

        [TestMethod]
        public void EvaluarPares_ComprasDeHaceMasDe24Meses_NoCuentan()
        {
            Compras("1001", "CREMA500", 50m, 800, 770, 40);
            Assert.AreEqual(0, Evaluar().Count);
        }

        [TestMethod]
        public void EvaluarPares_RitmoSemanal_NoSeAvisaNunca()
        {
            // Carlos (25/09/26): quien compra cada semana no necesita recordatorio, ni a los 10 días ni a los 18
            Compras("1001", "CREMA500", 50m, 31, 24, 17, 10);
            Assert.AreEqual(0, Evaluar().Count);

            compras.Clear();
            Compras("1001", "CREMA500", 50m, 39, 32, 25, 18);
            Assert.AreEqual(0, Evaluar().Count);

            // Cada 14 días ya no es semanal: se avisa a partir de 1,25 veces el ritmo
            compras.Clear();
            Compras("1001", "CREMA500", 50m, 60, 46, 32, 18);
            Assert.AreEqual(14, Evaluar().Single().IntervaloDias);
        }

        [TestMethod]
        public void NombreDePila_ElComercialPorSuNombre()
        {
            Assert.AreEqual("Lidia", CalculadoraReposicion.NombreDePila("LIDIA HERNÁNDEZ YAGÜE"));
            Assert.AreEqual("Mª José", CalculadoraReposicion.NombreDePila("Mª JOSÉ PÉREZ"));
            Assert.AreEqual("Ángela", CalculadoraReposicion.NombreDePila(" ángela  ruiz "));
            Assert.IsNull(CalculadoraReposicion.NombreDePila("  "));
        }

        [TestMethod]
        public void EvaluarPares_ImporteHabitual_EsLaMediaPorDiaDeCompra()
        {
            Compras("1001", "CREMA500", 30m, 100, 70);
            Compras("1001", "CREMA500", 90m, 40);
            Compras("1001", "CREMA500", 30m, 40); // Dos albaranes el mismo día: un solo día de compra
            Assert.AreEqual(60m, Evaluar().Single().ImporteHabitual);
        }

        [TestMethod]
        public void EvaluarPares_NoConsumibles_NoAparecen()
        {
            Compras("1001", "APARATO", 900m, 100, 70, 40);
            Compras("1001", "MUESTRA", 0m, 100, 70, 40);
            Compras("1001", "CUENTA7000", 10m, 100, 70, 40); // Línea de cuenta: no es un producto
            Assert.AreEqual(0, Evaluar().Count);
        }

        #endregion

        #region Cuándo no avisar

        [TestMethod]
        public void EvaluarPares_ProductoDeBaja_SeQuedaFueraConMotivo()
        {
            productos["CREMA500"].Estado = -1;
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_PRODUCTO_BAJA, Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void EvaluarPares_ProductoFicticio_SeQuedaFuera()
        {
            productos["CREMA500"].Ficticio = true;
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_PRODUCTO_BAJA, Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void EvaluarPares_ConPedidoPendienteDelProducto_SeQuedaFuera()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "1001  ", Producto = "CREMA500  ", Estado = -1, Fecha = HOY.AddDays(3) });
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_PEDIDO_PENDIENTE, Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void EvaluarPares_PedidoPendienteDeOtroCliente_NoLoQuita()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "2002", Producto = "CREMA500", Estado = 1, Fecha = HOY });
            Assert.IsTrue(Evaluar().Single().SeAvisaria);
        }

        [TestMethod]
        public void EvaluarPares_NotaDeEntregaPosteriorALaUltimaCompra_SeQuedaFuera()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "1001", Producto = "CREMA500", Estado = -2, Fecha = HOY.AddDays(-10) });
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_NOTA_ENTREGA, Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void EvaluarPares_NotaDeEntregaAnterior_NoLoQuita()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "1001", Producto = "CREMA500", Estado = -2, Fecha = HOY.AddDays(-200) });
            Assert.IsTrue(Evaluar().Single().SeAvisaria);
        }

        [TestMethod]
        public void EvaluarPares_DespuesComproOtroFormatoDeLaMismaMarca_SeQuedaFuera()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Compras("1001", "CREMA1000", 80m, 20);
            CandidatoReposicionDTO candidato = Evaluar().Single(c => c.Producto == "CREMA500");
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_SUSTITUTO + "CREMA1000", candidato.Motivo);
        }

        [TestMethod]
        public void EvaluarPares_ElSustitutoSeComproAntes_NoLoQuita()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Compras("1001", "CREMA1000", 80m, 60);
            Assert.IsTrue(Evaluar().Single(c => c.Producto == "CREMA500").SeAvisaria);
        }

        [TestMethod]
        public void EvaluarPares_DespuesComproOtraMarca_NoEsSustituto()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            Compras("1001", "CREMAOTRA", 80m, 20);
            Assert.IsTrue(Evaluar().Single(c => c.Producto == "CREMA500").SeAvisaria);
        }

        [TestMethod]
        public void EvaluarPares_SustitutoEnUnPedidoPendiente_SeQuedaFuera()
        {
            Compras("1001", "CREMA500", 50m, 100, 70, 40);
            pendientes.Add(new LineaPendienteReposicion { Cliente = "1001", Producto = "CREMA1000", Estado = 1, Fecha = HOY });
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_SUSTITUTO_PENDIENTE + "CREMA1000", Evaluar().Single().Motivo);
        }

        [TestMethod]
        public void AplicarSinStock_MarcaSoloLosQueSeAvisarian()
        {
            var candidatos = new List<CandidatoReposicionDTO>
            {
                new CandidatoReposicionDTO { Cliente = "1", Producto = "CREMA500" },
                new CandidatoReposicionDTO { Cliente = "2", Producto = "CREMA500", Motivo = CalculadoraReposicion.MOTIVO_PEDIDO_PENDIENTE },
                new CandidatoReposicionDTO { Cliente = "3", Producto = "TINTE" }
            };

            CalculadoraReposicion.AplicarSinStock(candidatos, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "crema500" });

            Assert.AreEqual(CalculadoraReposicion.MOTIVO_SIN_STOCK, candidatos[0].Motivo);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_PEDIDO_PENDIENTE, candidatos[1].Motivo);
            Assert.IsTrue(candidatos[2].SeAvisaria);
        }

        [TestMethod]
        public void ProductosSinStock_DisponibleEnSedesMenosPendienteMasTraspasos()
        {
            var resumen = new ResumenStocksProductos();
            resumen.StockSedes["CREMA500"] = 5;
            resumen.PendienteEntregarTotal["CREMA500"] = 5; // Todo comprometido
            resumen.StockSedes["TINTE"] = 2;
            resumen.PendienteEntregarTotal["TINTE"] = 3;
            resumen.PendienteReposicion["TINTE"] = 4; // Viene por traspaso
            resumen.StockSedes["CREMA1000"] = 1;

            HashSet<string> sinStock = CalculadoraReposicion.ProductosSinStock(resumen,
                new[] { "CREMA500", "TINTE", "CREMA1000", "NADA" });

            CollectionAssert.AreEquivalent(new[] { "CREMA500", "NADA" }, sinStock.ToList());
        }

        #endregion

        #region Agrupar por cliente

        private static CandidatoReposicionDTO Candidato(string cliente, string producto, decimal importe, string motivo = null)
            => new CandidatoReposicionDTO { Cliente = cliente, Producto = producto, ImporteHabitual = importe, Motivo = motivo };

        private static ClienteReposicion Cliente(string id, string email, short estado = 0, string vendedor = "LHY", string vendedorNombre = "Lidia")
            => new ClienteReposicion { Cliente = id, Nombre = "CLIENTE " + id, Email = email, Estado = estado, Vendedor = vendedor, VendedorNombre = vendedorNombre };

        private static string ClienteFueraDelControl(int desde = 1000)
        {
            for (int i = desde; ; i++)
            {
                if (!CalculadoraReposicion.EsGrupoControl(i.ToString()))
                {
                    return i.ToString();
                }
            }
        }

        private static string ClienteDelControl()
        {
            for (int i = 1000; ; i++)
            {
                if (CalculadoraReposicion.EsGrupoControl(i.ToString()))
                {
                    return i.ToString();
                }
            }
        }

        [TestMethod]
        public void Agrupar_UnCorreoPorClienteConComoMuchoTresProductosLosDeMasImporte()
        {
            string c = ClienteFueraDelControl();
            var candidatos = new List<CandidatoReposicionDTO>
            {
                Candidato(c, "A", 10m), Candidato(c, "B", 40m), Candidato(c, "C", 30m), Candidato(c, "D", 20m)
            };
            var clientes = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase) { [c] = Cliente(c, "ana@x.es") };

            ResultadoRecordatorioReposicionDTO resultado = CalculadoraReposicion.Agrupar(candidatos, clientes, HOY);

            RecordatorioReposicionClienteDTO correo = resultado.SeAvisarian.Single();
            CollectionAssert.AreEqual(new[] { "B", "C", "D" }, correo.Productos.Select(p => p.Producto).ToList());
            Assert.AreEqual("A", resultado.Descartes.Single().Producto);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_MAXIMO_PRODUCTOS, resultado.Descartes.Single().Motivo);
            Assert.AreEqual("Lidia", correo.VendedorNombre);
        }

        [TestMethod]
        public void Agrupar_ClienteSinCorreoODeBaja_SeQuedaFuera()
        {
            string sinCorreo = ClienteFueraDelControl(1000);
            string deBaja = ClienteFueraDelControl(int.Parse(sinCorreo) + 1);
            var candidatos = new List<CandidatoReposicionDTO> { Candidato(sinCorreo, "A", 10m), Candidato(deBaja, "A", 10m), Candidato("9999999", "A", 1m) };
            var clientes = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase)
            {
                [sinCorreo] = Cliente(sinCorreo, " "),
                [deBaja] = Cliente(deBaja, "baja@x.es", estado: 8)
            };

            ResultadoRecordatorioReposicionDTO resultado = CalculadoraReposicion.Agrupar(candidatos, clientes, HOY);

            Assert.AreEqual(0, resultado.SeAvisarian.Count);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_SIN_CORREO, resultado.Descartes.Single(d => d.Cliente == sinCorreo).Motivo);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_CLIENTE_BAJA, resultado.Descartes.Single(d => d.Cliente == deBaja).Motivo);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_CLIENTE_NO_ENCONTRADO, resultado.Descartes.Single(d => d.Cliente == "9999999").Motivo);
        }

        [TestMethod]
        public void Agrupar_DosClientesConElMismoCorreo_SoloUnCorreo()
        {
            string uno = ClienteFueraDelControl(1000);
            string otro = ClienteFueraDelControl(int.Parse(uno) + 1);
            var candidatos = new List<CandidatoReposicionDTO>
            {
                Candidato(uno, "A", 10m), Candidato(otro, "A", 10m), Candidato(otro, "B", 10m)
            };
            var clientes = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase)
            {
                [uno] = Cliente(uno, "Salon@x.es"),
                [otro] = Cliente(otro, "salon@x.es ")
            };

            ResultadoRecordatorioReposicionDTO resultado = CalculadoraReposicion.Agrupar(candidatos, clientes, HOY);

            Assert.AreEqual(otro, resultado.SeAvisarian.Single().Cliente);
            Assert.AreEqual(CalculadoraReposicion.MOTIVO_CORREO_DUPLICADO + otro, resultado.Descartes.Single().Motivo);
        }

        [TestMethod]
        public void Agrupar_ClienteDelGrupoDeControl_NoSeLeEscribe()
        {
            string control = ClienteDelControl();
            var candidatos = new List<CandidatoReposicionDTO> { Candidato(control, "A", 10m) };
            var clientes = new Dictionary<string, ClienteReposicion>(StringComparer.OrdinalIgnoreCase) { [control] = Cliente(control, "c@x.es") };

            ResultadoRecordatorioReposicionDTO resultado = CalculadoraReposicion.Agrupar(candidatos, clientes, HOY);

            Assert.AreEqual(0, resultado.SeAvisarian.Count);
            Assert.AreEqual(control, resultado.GrupoControl.Single().Cliente);
        }

        [TestMethod]
        public void EsGrupoControl_EsEstableYRondaElDiezPorCiento()
        {
            int enControl = Enumerable.Range(10000, 5000).Count(i => CalculadoraReposicion.EsGrupoControl(i.ToString()));
            Assert.IsTrue(enControl > 350 && enControl < 650, $"{enControl} de 5000");
            Assert.AreEqual(CalculadoraReposicion.EsGrupoControl("15191"), CalculadoraReposicion.EsGrupoControl(" 15191 "));
            Assert.IsFalse(CalculadoraReposicion.EsGrupoControl("15191", 0));
        }

        [TestMethod]
        public void NombreVendedor_ElVendedorGeneralNoEsUnaPersona()
        {
            Assert.IsNull(CalculadoraReposicion.NombreVendedor(Cliente("1", "a@x.es", vendedor: "NV ", vendedorNombre: "General")));
            Assert.AreEqual("Lidia", CalculadoraReposicion.NombreVendedor(Cliente("1", "a@x.es")));
        }

        #endregion

        #region Consumibles

        [TestMethod]
        public void Consumibles_PorDefecto()
        {
            Assert.IsTrue(CONSUMIBLES.EsConsumible("COS", "125"));
            Assert.IsTrue(CONSUMIBLES.EsConsumible("COS ", "201"));
            Assert.IsFalse(CONSUMIBLES.EsConsumible("COS", "MMP"), "Muestras y material promocional");
            Assert.IsTrue(CONSUMIBLES.EsConsumible("PEL", "TIN"));
            Assert.IsFalse(CONSUMIBLES.EsConsumible("PEL", "PEI"));
            Assert.IsTrue(CONSUMIBLES.EsConsumible("ACC", "002"));
            // Carlos (25/09/26): manicura, pedicura y utillaje también se compran cíclicamente
            Assert.IsTrue(CONSUMIBLES.EsConsumible("ACC", "005"), "Manicura");
            Assert.IsTrue(CONSUMIBLES.EsConsumible("ACC", "006"), "Pedicura");
            Assert.IsTrue(CONSUMIBLES.EsConsumible("PEL", "UTJ"), "Utillaje");
            Assert.IsTrue(CONSUMIBLES.EsConsumible("PEL", "PEL"), "Utillaje.");
            Assert.IsFalse(CONSUMIBLES.EsConsumible("ACC", "001"), "Vestuario");
            Assert.IsFalse(CONSUMIBLES.EsConsumible("APA", "001"));
            Assert.IsFalse(CONSUMIBLES.EsConsumible("CUR", "INI"));
        }

        [TestMethod]
        public void Consumibles_DesdeParametro()
        {
            ConsumiblesReposicion lista = ConsumiblesReposicion.Leer("PEL; ACC/002 ; -PEL/APA");
            Assert.IsTrue(lista.EsConsumible("PEL", "TIN"));
            Assert.IsFalse(lista.EsConsumible("PEL", "APA"));
            Assert.IsTrue(lista.EsConsumible("ACC", "002"));
            Assert.IsFalse(lista.EsConsumible("COS", "125"));
        }

        [TestMethod]
        public void Consumibles_ParametroVacioOInvalido_UsaLaListaPorDefecto()
        {
            Assert.AreEqual(ConsumiblesReposicion.POR_DEFECTO, ConsumiblesReposicion.Leer("").Definicion);
            Assert.AreEqual(ConsumiblesReposicion.POR_DEFECTO, ConsumiblesReposicion.Leer(" -COS/MMP ").Definicion);
        }

        #endregion
    }
}
