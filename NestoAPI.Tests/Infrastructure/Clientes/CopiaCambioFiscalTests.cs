using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes.CambioFiscal;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>NestoAPI#876: qué se copia del cliente anterior al nuevo cuando cambia de forma fiscal (caso 37283 → 42178).</summary>
    [TestClass]
    public class CopiaCambioFiscalTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26);

        private static FotoClienteCambioFiscal Anterior() => new FotoClienteCambioFiscal
        {
            Cliente = "37283",
            Fichas = new List<FichaCambioFiscal>
            {
                new FichaCambioFiscal { Contacto = "0", ClientePrincipal = true, Nombre = "FOLCH GIRBAU", Estado = 67, Vendedor = "CAM", Iva = "E52",
                    Comentarios = "Distribuidor de Sabadell", DiasEnServir = "11111", Ruta = "AT", PeriodoFacturacion = "NRM", NoComisiona = 0, MantenerJunto = false, Ccc = "1" }
            },
            VendedoresGrupo = new List<VendedorGrupoCambioFiscal> { new VendedorGrupoCambioFiscal { Contacto = "0", GrupoProducto = "PEL", Vendedor = "CAM", Estado = 0 } },
            Condiciones = new List<CondPagoCambioFiscal> { new CondPagoCambioFiscal { Contacto = "0", FormaPago = "RCB", PlazosPago = "1/30", ImporteMinimo = 0, CccObligatorio = true } },
            Descuentos = new List<DescuentoCambioFiscal>
            {
                new DescuentoCambioFiscal { Familia = "LISAP", Descuento = 0.5M },
                new DescuentoCambioFiscal { Familia = "EVA", Descuento = 0.5M },
                new DescuentoCambioFiscal { Producto = "43165", Precio = 9.95M },
                new DescuentoCambioFiscal { Producto = "40000", Descuento = 0.3M, FechaHasta = new DateTime(2026, 1, 31) }
            },
            Ofertas = new List<OfertaCambioFiscal> { new OfertaCambioFiscal { Familia = "LISAP", CantidadConPrecio = 6, CantidadRegalo = 1 } }
        };

        private static FotoClienteCambioFiscal Nuevo() => new FotoClienteCambioFiscal
        {
            Cliente = "42178",
            Fichas = new List<FichaCambioFiscal>
            {
                new FichaCambioFiscal { Contacto = "0", ClientePrincipal = true, Nombre = "CUSIDÓ FOLCH", Estado = 0, Vendedor = "NV", Iva = "G21",
                    DiasEnServir = "11111", Ruta = "FW", PeriodoFacturacion = "NRM", Ccc = "1" }
            },
            Condiciones = new List<CondPagoCambioFiscal> { new CondPagoCambioFiscal { Contacto = "0", FormaPago = "EFC", PlazosPago = "CONTADO", ImporteMinimo = 0 } }
        };

        private static BloqueCambioFiscalDTO Bloque(PlanCopiaCambioFiscal plan, string bloque) => plan.Bloques.Single(b => b.Bloque == bloque);

        [TestMethod]
        public void Planificar_CasoReal_CopiaEstadoVendedorIvaPlazosDescuentosOfertasYFicha()
        {
            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(Anterior(), Nuevo(), null, HOY);

            CambioFichaCambioFiscal ficha = plan.Fichas.Single();
            Assert.AreEqual("0", ficha.Contacto);
            Assert.AreEqual((short)67, ficha.Estado);
            Assert.AreEqual("CAM", ficha.Vendedor);
            Assert.AreEqual("E52", ficha.Iva);
            Assert.AreEqual("Distribuidor de Sabadell", ficha.Comentarios);
            Assert.AreEqual("AT", ficha.Ruta);
            Assert.IsNull(ficha.DiasEnServir, "Ya los tiene iguales");
            Assert.AreEqual("PEL", plan.VendedoresGrupo.Single().GrupoProducto);
            Assert.AreEqual("EFC", plan.CondicionesAQuitar.Single().FormaPago);
            Assert.AreEqual("RCB", plan.CondicionesAPoner.Single().FormaPago);
            Assert.AreEqual(3, plan.Descuentos.Count, "El caducado no se copia");
            Assert.AreEqual(1, plan.Ofertas.Count);
            Assert.IsTrue(plan.CambiaFicha);
            CollectionAssert.Contains(Bloque(plan, BloquesCambioFiscal.FISCALIDAD).Cambios, "Contacto 0: IVA G21 → E52");
            Assert.IsTrue(Bloque(plan, BloquesCambioFiscal.DESCUENTOS).Avisos.Any(a => a.Contains("caducado")));
        }

        [TestMethod]
        public void Planificar_DosVeces_LaSegundaNoHaceNada()
        {
            FotoClienteCambioFiscal anterior = Anterior();
            FotoClienteCambioFiscal nuevo = Nuevo();
            Aplicar(nuevo, ReglaCopiaCambioFiscal.Planificar(anterior, nuevo, null, HOY));

            PlanCopiaCambioFiscal segunda = ReglaCopiaCambioFiscal.Planificar(anterior, nuevo, null, HOY);

            Assert.IsFalse(segunda.HayCambios, string.Join(" | ", segunda.Bloques.SelectMany(b => b.Cambios)));
            Assert.IsFalse(segunda.CambiaFicha);
        }

        [TestMethod]
        public void Planificar_SoloLosBloquesPedidos()
        {
            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(Anterior(), Nuevo(), new[] { BloquesCambioFiscal.FISCALIDAD, BloquesCambioFiscal.DESCUENTOS }, HOY);

            CollectionAssert.AreEqual(new[] { BloquesCambioFiscal.FISCALIDAD, BloquesCambioFiscal.DESCUENTOS }, plan.Bloques.Select(b => b.Bloque).ToArray());
            CambioFichaCambioFiscal ficha = plan.Fichas.Single();
            Assert.AreEqual("E52", ficha.Iva);
            Assert.IsNull(ficha.Vendedor);
            Assert.IsNull(ficha.Estado);
            Assert.IsFalse(plan.CondicionesAPoner.Any());
            Assert.IsFalse(plan.VendedoresGrupo.Any());
        }

        [TestMethod]
        public void Planificar_SinIvaEnElAnterior_NoLoCopiaYAvisa()
        {
            FotoClienteCambioFiscal anterior = Anterior();
            anterior.Fichas[0].Iva = null;

            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(anterior, Nuevo(), new[] { BloquesCambioFiscal.FISCALIDAD }, HOY);

            Assert.IsFalse(plan.CambiaFicha);
            StringAssert.Contains(Bloque(plan, BloquesCambioFiscal.FISCALIDAD).Avisos.Single(), "no tiene tipo de IVA");
        }

        [TestMethod]
        public void Planificar_AnteriorDeBaja_NoCopiaElEstadoPeroSiElVendedor()
        {
            FotoClienteCambioFiscal anterior = Anterior();
            anterior.Fichas[0].Estado = -1;

            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(anterior, Nuevo(), new[] { BloquesCambioFiscal.COMERCIAL }, HOY);

            Assert.IsNull(plan.Fichas.Single().Estado);
            Assert.AreEqual("CAM", plan.Fichas.Single().Vendedor);
            StringAssert.Contains(Bloque(plan, BloquesCambioFiscal.COMERCIAL).Avisos.Single(), "de baja");
        }

        [TestMethod]
        public void Planificar_FormaDePagoConCuentaYElNuevoSinCcc_AvisaQueLaCuentaNoSeCopia()
        {
            FotoClienteCambioFiscal nuevo = Nuevo();
            nuevo.Fichas[0].Ccc = null;

            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(Anterior(), nuevo, new[] { BloquesCambioFiscal.CONDICIONES_PAGO }, HOY);

            StringAssert.Contains(Bloque(plan, BloquesCambioFiscal.CONDICIONES_PAGO).Avisos.Single(), "necesita cuenta bancaria");
        }

        [TestMethod]
        public void Planificar_DescuentoQueElNuevoYaTieneDistinto_NoLoTocaYAvisa()
        {
            FotoClienteCambioFiscal nuevo = Nuevo();
            nuevo.Descuentos.Add(new DescuentoCambioFiscal { Familia = "LISAP ", Descuento = 0.4M });

            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(Anterior(), nuevo, new[] { BloquesCambioFiscal.DESCUENTOS }, HOY);

            Assert.IsFalse(plan.Descuentos.Any(d => d.Familia == "LISAP"));
            Assert.AreEqual(2, plan.Descuentos.Count);
            Assert.IsTrue(Bloque(plan, BloquesCambioFiscal.DESCUENTOS).Avisos.Any(a => a.Contains("ya tiene otro (40 %)")));
        }

        [TestMethod]
        public void Planificar_DescuentoDeUnContactoQueElNuevoNoTiene_NoSeCopia()
        {
            FotoClienteCambioFiscal anterior = Anterior();
            anterior.Descuentos = new List<DescuentoCambioFiscal> { new DescuentoCambioFiscal { Familia = "LISAP", Descuento = 0.5M, Contacto = "2" } };

            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(anterior, Nuevo(), new[] { BloquesCambioFiscal.DESCUENTOS }, HOY);

            Assert.IsFalse(plan.Descuentos.Any());
            StringAssert.Contains(Bloque(plan, BloquesCambioFiscal.DESCUENTOS).Avisos.Single(), "contacto 2");
        }

        [TestMethod]
        public void Planificar_ComentariosPropiosDelNuevo_NoSePisan()
        {
            FotoClienteCambioFiscal nuevo = Nuevo();
            nuevo.Fichas[0].Comentarios = "Alta nueva";

            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(Anterior(), nuevo, new[] { BloquesCambioFiscal.FICHA }, HOY);

            Assert.IsNull(plan.Fichas.Single().Comentarios);
            StringAssert.Contains(Bloque(plan, BloquesCambioFiscal.FICHA).Avisos.Single(), "ya tiene comentarios propios");
        }

        [TestMethod]
        public void Planificar_CadaContactoDelNuevoCopiaDelMismoContactoOSiNoDelPrincipal()
        {
            FotoClienteCambioFiscal anterior = Anterior();
            anterior.Fichas.Add(new FichaCambioFiscal { Contacto = "1", Estado = 0, Vendedor = "JE", Iva = "G21" });
            FotoClienteCambioFiscal nuevo = Nuevo();
            nuevo.Fichas.Add(new FichaCambioFiscal { Contacto = "1", Estado = 0, Vendedor = "NV", Iva = "G21" });
            nuevo.Fichas.Add(new FichaCambioFiscal { Contacto = "2", Estado = 0, Vendedor = "NV", Iva = "G21" });

            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(anterior, nuevo, new[] { BloquesCambioFiscal.COMERCIAL }, HOY);

            Assert.AreEqual("JE", plan.Fichas.Single(f => f.Contacto == "1").Vendedor);
            Assert.AreEqual("CAM", plan.Fichas.Single(f => f.Contacto == "2").Vendedor);
        }

        [TestMethod]
        public void Normalizar_SinBloques_Todos_YLosQueNoExistenAparte()
        {
            CollectionAssert.AreEqual(BloquesCambioFiscal.TODOS.ToArray(), BloquesCambioFiscal.Normalizar(null, out List<string> d1).ToArray());
            Assert.AreEqual(0, d1.Count);
            CollectionAssert.AreEqual(new[] { BloquesCambioFiscal.FISCALIDAD }, BloquesCambioFiscal.Normalizar(new[] { "fiscalidad", "Iban" }, out List<string> d2).ToArray());
            CollectionAssert.AreEqual(new[] { "Iban" }, d2.ToArray());
        }

        [TestMethod]
        public void DecidirEnlace_EnlazadosVale_SinEnlaceConOrigenSeCrea_SinOrigen409()
        {
            Assert.IsTrue(ReglaCopiaCambioFiscal.DecidirEnlace("37283", "42178", true, true, new[] { "37283" }, null).Enlazados);
            DecisionEnlaceCambioFiscal crear = ReglaCopiaCambioFiscal.DecidirEnlace("37283", "42178", true, true, new string[0], "cambio-fiscal");
            Assert.IsTrue(crear.Vale && crear.CrearEnlace);
            DecisionEnlaceCambioFiscal no = ReglaCopiaCambioFiscal.DecidirEnlace("37283", "42178", true, true, new string[0], "WEB");
            Assert.AreEqual(409, no.Estado);
            StringAssert.Contains(no.Motivo, "no consta como el 37283 con otra forma fiscal");
            Assert.AreEqual(409, ReglaCopiaCambioFiscal.DecidirEnlace("37283", "42178", true, true, new[] { "11111" }, "CAMBIO-FISCAL").Estado);
            Assert.AreEqual(404, ReglaCopiaCambioFiscal.DecidirEnlace("37283", "42178", false, true, null, null).Estado);
            Assert.AreEqual(400, ReglaCopiaCambioFiscal.DecidirEnlace("37283", "37283 ", true, true, null, null).Estado);
        }

        [TestMethod]
        public void MotivoSinPermiso_OficinaVendedorYJefeSi_OtroNo()
        {
            Assert.IsNull(ReglaCopiaCambioFiscal.MotivoSinPermiso(true, null, null, "37283", "CAM"));
            Assert.IsNull(ReglaCopiaCambioFiscal.MotivoSinPermiso(false, "CAM", null, "37283", "CAM "));
            Assert.IsNull(ReglaCopiaCambioFiscal.MotivoSinPermiso(false, "JE", new List<string> { "JE", "CAM" }, "37283", "CAM"));
            StringAssert.Contains(ReglaCopiaCambioFiscal.MotivoSinPermiso(false, "PA", null, "37283", "CAM"), "Solo Administración, Dirección, Informática");
            Assert.IsNotNull(ReglaCopiaCambioFiscal.MotivoSinPermiso(false, null, null, "37283", "CAM"));
        }

        [TestMethod]
        public void PedidoPendiente_ConPicking_SePuedeIntentarYLoAvisa()
        {
            CabPedidoVta cab = new CabPedidoVta { Empresa = "1", Número = 931691, Contacto = "0" };
            List<LinPedidoVta> lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 1, Estado = 1, Cantidad = 2, Picking = 100198, Base_Imponible = 10, Total = 12.62M, Producto = "A" },
                new LinPedidoVta { Nº_Orden = 2, Estado = -1, Cantidad = 1, Base_Imponible = 5, Total = 6.31M, Producto = "B" }
            };

            PedidoPendienteCambioFiscalDTO p = ReglaCopiaCambioFiscal.PedidoPendiente(cab, lineas, 0, new List<int>(), 0);

            Assert.IsTrue(p.SePuedePasar);
            Assert.IsTrue(p.TienePicking);
            CollectionAssert.AreEqual(new[] { 100198 }, p.Pickings.ToArray());
            Assert.AreEqual(15M, p.Importe);
            Assert.AreEqual(2, p.Lineas);
            StringAssert.Contains(p.Motivo, "Tiene picking");
        }

        [TestMethod]
        public void PedidoPendiente_ConEnvioDeAgencia_NoSePuede()
        {
            CabPedidoVta cab = new CabPedidoVta { Empresa = "1", Número = 931691, Contacto = "0" };
            List<LinPedidoVta> lineas = new List<LinPedidoVta> { new LinPedidoVta { Nº_Orden = 1, Estado = 1, Cantidad = 2, Picking = 1, Producto = "A" } };

            PedidoPendienteCambioFiscalDTO p = ReglaCopiaCambioFiscal.PedidoPendiente(cab, lineas, 0, new List<int> { 251300 }, 0);

            Assert.IsFalse(p.SePuedePasar);
            StringAssert.Contains(p.Motivo, "251300");
        }

        /// <summary>Lo que haría el repositorio con el plan, sobre la foto en memoria.</summary>
        private static void Aplicar(FotoClienteCambioFiscal nuevo, PlanCopiaCambioFiscal plan)
        {
            foreach (CambioFichaCambioFiscal c in plan.Fichas)
            {
                FichaCambioFiscal f = nuevo.Fichas.Single(x => x.Contacto == c.Contacto);
                f.Estado = c.Estado ?? f.Estado;
                f.Vendedor = c.Vendedor ?? f.Vendedor;
                f.Iva = c.Iva ?? f.Iva;
                f.Comentarios = c.Comentarios ?? f.Comentarios;
                f.DiasEnServir = c.DiasEnServir ?? f.DiasEnServir;
                f.Ruta = c.Ruta ?? f.Ruta;
                f.PeriodoFacturacion = c.PeriodoFacturacion ?? f.PeriodoFacturacion;
                f.NoComisiona = c.NoComisiona ?? f.NoComisiona;
                f.MantenerJunto = c.MantenerJunto ?? f.MantenerJunto;
            }
            foreach (VendedorGrupoCambioFiscal v in plan.VendedoresGrupo)
            {
                _ = nuevo.VendedoresGrupo.RemoveAll(x => x.Contacto == v.Contacto && x.GrupoProducto == v.GrupoProducto);
                nuevo.VendedoresGrupo.Add(v);
            }
            _ = nuevo.Condiciones.RemoveAll(c => plan.CondicionesAQuitar.Contains(c));
            nuevo.Condiciones.AddRange(plan.CondicionesAPoner);
            nuevo.Descuentos.AddRange(plan.Descuentos);
            nuevo.Ofertas.AddRange(plan.Ofertas);
        }
    }
}
