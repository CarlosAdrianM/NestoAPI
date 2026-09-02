using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Security.Claims;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#446: la persona con cargo "Pedidos sin ver precios" pide sin enterarse de a
    /// qué precio compra. La garantía es del servidor.
    /// </summary>
    [TestClass]
    public class PoliticaPreciosOcultosTests
    {
        private static ClaimsIdentity IdentidadCliente(PoliticaPreciosOcultos.NivelPrecios nivel)
        {
            List<Claim> claims = new List<Claim> { new Claim("cliente", "15191") };
            if (nivel != PoliticaPreciosOcultos.NivelPrecios.Completo)
            {
                claims.Add(new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, nivel.ToString()));
            }
            return new ClaimsIdentity(claims, "JWT");
        }

        [TestMethod]
        public void NivelDe_SaleDelClaimYSinClaimEsCompleto()
        {
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.SinPrecios,
                PoliticaPreciosOcultos.NivelDe(IdentidadCliente(PoliticaPreciosOcultos.NivelPrecios.SinPrecios)));
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.SinDescuentos,
                PoliticaPreciosOcultos.NivelDe(IdentidadCliente(PoliticaPreciosOcultos.NivelPrecios.SinDescuentos)));
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.Completo,
                PoliticaPreciosOcultos.NivelDe(IdentidadCliente(PoliticaPreciosOcultos.NivelPrecios.Completo)));
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.Completo, PoliticaPreciosOcultos.NivelDe(null));
            // Un claim manipulado con un valor inventado no abre ni cierra nada raro
            ClaimsIdentity raro = new ClaimsIdentity(new[] { new Claim(PoliticaPreciosOcultos.CLAIM_NIVEL_PRECIOS, "loquesea") }, "JWT");
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.Completo, PoliticaPreciosOcultos.NivelDe(raro));
        }

        [TestMethod]
        public void OcultaImportes_EnLosDosNivelesRestringidos()
        {
            Assert.IsTrue(PoliticaPreciosOcultos.OcultaImportes(IdentidadCliente(PoliticaPreciosOcultos.NivelPrecios.SinPrecios)));
            Assert.IsTrue(PoliticaPreciosOcultos.OcultaImportes(IdentidadCliente(PoliticaPreciosOcultos.NivelPrecios.SinDescuentos)));
            Assert.IsFalse(PoliticaPreciosOcultos.OcultaImportes(IdentidadCliente(PoliticaPreciosOcultos.NivelPrecios.Completo)));
            Assert.IsTrue(PoliticaPreciosOcultos.EsUsuarioSinPrecios(IdentidadCliente(PoliticaPreciosOcultos.NivelPrecios.SinPrecios)));
            Assert.IsFalse(PoliticaPreciosOcultos.EsUsuarioSinPrecios(IdentidadCliente(PoliticaPreciosOcultos.NivelPrecios.SinDescuentos)));
        }

        [TestMethod]
        public void NivelMasRestrictivo_ConCargosIncoherentes_MandaElMasRestrictivo()
        {
            // El mismo correo con 22 (factura electrónica), 31 (sin descuentos) y 30 (sin precios)
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.SinPrecios,
                PoliticaPreciosOcultos.NivelMasRestrictivo(new List<short?> { 22, 31, 30 }));
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.SinDescuentos,
                PoliticaPreciosOcultos.NivelMasRestrictivo(new List<short?> { 22, 31 }));
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.SinDescuentos,
                PoliticaPreciosOcultos.NivelMasRestrictivo(new List<short?> { 11, 31 }));
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.Completo,
                PoliticaPreciosOcultos.NivelMasRestrictivo(new List<short?> { 22, 14, null }));
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.Completo,
                PoliticaPreciosOcultos.NivelMasRestrictivo(new List<short?>()));
            Assert.AreEqual(PoliticaPreciosOcultos.NivelPrecios.Completo, PoliticaPreciosOcultos.NivelMasRestrictivo(null));
        }

        [TestMethod]
        public void AplicarNivel_SinDescuentos_DejaLaTarifaSinDescuentoNiPrecioEspecial()
        {
            // Precio especial de cliente 25,95 sobre una tarifa de 34,95 con un 15 %: ve 34,95 y nada más
            ProductoPlantillaDTO producto = new ProductoPlantillaDTO { producto = "40056", precio = 25.95m, descuento = 0.15m, aplicarDescuento = true };

            PoliticaPreciosOcultos.AplicarNivel(producto, PoliticaPreciosOcultos.NivelPrecios.SinDescuentos, tarifaProfesional: 34.95m);

            Assert.AreEqual(34.95m, producto.precio);
            Assert.AreEqual(0m, producto.descuento);
            Assert.IsFalse(producto.aplicarDescuento);
            Assert.IsTrue(producto.descuentoOculto);
            Assert.IsFalse(producto.precioOculto);
        }

        [TestMethod]
        public void AplicarNivel_Completo_NoTocaNada()
        {
            ProductoPlantillaDTO producto = new ProductoPlantillaDTO { precio = 25.95m, descuento = 0.15m, aplicarDescuento = true };

            PoliticaPreciosOcultos.AplicarNivel(producto, PoliticaPreciosOcultos.NivelPrecios.Completo, 34.95m);

            Assert.AreEqual(25.95m, producto.precio);
            Assert.AreEqual(0.15m, producto.descuento);
            Assert.IsFalse(producto.descuentoOculto);
            Assert.IsFalse(producto.precioOculto);
        }

        [TestMethod]
        public void ForzarFormaDePagoHabitual_QuitaTarjetaYFormaElegida()
        {
            // Lo que venga en el cuerpo se ignora: ni tarjeta, ni tarjeta guardada, ni forma elegida
            PedidoClienteRequest peticion = new PedidoClienteRequest
            {
                PagarConTarjeta = true,
                PagarConTarjetaGuardada = true,
                TarjetaId = 7,
                FormaPago = "TRN",
                PlazosPago = "1/30"
            };

            PoliticaPreciosOcultos.ForzarFormaDePagoHabitual(peticion);

            Assert.IsFalse(peticion.PagarConTarjeta);
            Assert.IsFalse(peticion.PagarConTarjetaGuardada);
            Assert.IsNull(peticion.TarjetaId);
            Assert.IsNull(peticion.FormaPago);
            Assert.IsNull(peticion.PlazosPago);
        }

        [TestMethod]
        public void ResolverFormaDePagoHabitual_LaDeLaFichaConSusPlazos()
        {
            // Las condiciones del canal APP ya son "lo de la ficha + tarjeta" (PoliticaPagoCanal):
            // la habitual es la que no es tarjeta, con los plazos que no son prepago
            CondicionesPagoResponse condiciones = new CondicionesPagoResponse
            {
                FormasPago = new List<FormaPagoDTO>
                {
                    new FormaPagoDTO { formaPago = "TAR" },
                    new FormaPagoDTO { formaPago = "RCB" }
                },
                PlazosPago = new List<PlazoPagoDTO>
                {
                    new PlazoPagoDTO { plazoPago = "PRE" },
                    new PlazoPagoDTO { plazoPago = "1/30" }
                },
                PlazoPagoRecomendado = "PRE"
            };

            PoliticaPreciosOcultos.FormaYPlazos habitual = PoliticaPreciosOcultos.ResolverFormaDePagoHabitual(condiciones);

            Assert.AreEqual("RCB", habitual.FormaPago);
            Assert.AreEqual("1/30", habitual.PlazosPago);
        }

        [TestMethod]
        public void ResolverFormaDePagoHabitual_SinPlazosAparteDelPrepago_SeQuedaConElRecomendado()
        {
            CondicionesPagoResponse condiciones = new CondicionesPagoResponse
            {
                FormasPago = new List<FormaPagoDTO> { new FormaPagoDTO { formaPago = "TAR" }, new FormaPagoDTO { formaPago = "TRN" } },
                PlazosPago = new List<PlazoPagoDTO> { new PlazoPagoDTO { plazoPago = "PRE" } },
                PlazoPagoRecomendado = "PRE"
            };

            PoliticaPreciosOcultos.FormaYPlazos habitual = PoliticaPreciosOcultos.ResolverFormaDePagoHabitual(condiciones);

            Assert.AreEqual("TRN", habitual.FormaPago);
            Assert.AreEqual("PRE", habitual.PlazosPago);
        }

        [TestMethod]
        public void ResolverFormaDePagoHabitual_SoloTarjeta_NoHayHabitual()
        {
            // Ficha al contado, o con deuda (la política solo deja tarjeta): el pedido se rechaza
            CondicionesPagoResponse condiciones = new CondicionesPagoResponse
            {
                FormasPago = new List<FormaPagoDTO> { new FormaPagoDTO { formaPago = "TAR" } },
                PlazosPago = new List<PlazoPagoDTO> { new PlazoPagoDTO { plazoPago = "PRE" } }
            };

            Assert.IsNull(PoliticaPreciosOcultos.ResolverFormaDePagoHabitual(condiciones));
            Assert.IsNull(PoliticaPreciosOcultos.ResolverFormaDePagoHabitual(null));
        }

        [TestMethod]
        public void OcultarPrecio_DejaElProductoSinPrecioNiDescuento()
        {
            ProductoPlantillaDTO producto = new ProductoPlantillaDTO { producto = "12345", precio = 32.95m, descuento = 0.15m, aplicarDescuento = true };

            PoliticaPreciosOcultos.OcultarPrecio(producto);

            Assert.AreEqual(0m, producto.precio);
            Assert.AreEqual(0m, producto.descuento);
            Assert.IsFalse(producto.aplicarDescuento);
            Assert.IsTrue(producto.precioOculto);
            Assert.AreEqual("12345", producto.producto, "lo que no es dinero se queda");
        }

        [TestMethod]
        public void OcultarImportes_DejaLaRespuestaSinNingunImporte()
        {
            PedidoClienteResponse respuesta = new PedidoClienteResponse
            {
                Numero = 925300,
                BaseImponible = 100m,
                Total = 121m,
                Portes = 5.5m,
                Lineas = new List<LineaPedidoClienteResponse>
                {
                    new LineaPedidoClienteResponse { Producto = "12345", Cantidad = 2, PrecioUnitario = 10m, Descuento = 0.1m, BaseImponible = 18m, Total = 21.78m }
                }
            };

            PoliticaPreciosOcultos.OcultarImportes(respuesta);

            Assert.IsTrue(respuesta.ImportesOcultos);
            Assert.AreEqual(0m, respuesta.BaseImponible);
            Assert.AreEqual(0m, respuesta.Total);
            Assert.AreEqual(0m, respuesta.Portes);
            LineaPedidoClienteResponse linea = System.Linq.Enumerable.First(respuesta.Lineas);
            Assert.AreEqual(0m, linea.PrecioUnitario);
            Assert.AreEqual(0m, linea.Descuento);
            Assert.AreEqual(0m, linea.BaseImponible);
            Assert.AreEqual(0m, linea.Total);
            Assert.AreEqual(925300, respuesta.Numero);
            Assert.AreEqual((short)2, linea.Cantidad, "producto y cantidad sí se cuentan");
        }
    }
}
