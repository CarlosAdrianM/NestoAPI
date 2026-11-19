using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using NestoAPI.Models.Informes;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#874 (19/11/26): la Dirección 2 (piso, rótulo, nave) llega a donde la ve un repartidor, un mozo o un cliente: packing de
    /// Ariadna y en PDF, correo del pedido y dirección de ENTREGA de los documentos (la FISCAL no cambia). Sin Dirección 2 o sin columnas,
    /// lo de siempre.
    /// </summary>
    [TestClass]
    public class Direccion2DestinosTests
    {
        [TestCleanup]
        public void Limpiar()
        {
            Direccion2ClienteSql.LectorParaTests = null;
            Direccion2ClienteSql.LectorClientesParaTests = null;
        }

        private static Dictionary<string, Direccion2Contacto> Filas(params (string cliente, string contacto, string d2)[] filas)
            => filas.ToDictionary(f => Direccion2ClienteSql.ClaveContacto(f.cliente, f.contacto),
                f => new Direccion2Contacto { Cliente = f.cliente, Contacto = f.contacto, Direccion2 = f.d2 });

        // ---- Packing de Ariadna (mozo) ----

        [TestMethod]
        public void PackingAriadna_CadaEntregaConSuDireccionCompleta()
        {
            var filas = new List<FilaPackingAlmacen>
            {
                new FilaPackingAlmacen { Cliente = "15191", Contacto = "0", Direccion = "CALLE MAYOR, 3" },
                new FilaPackingAlmacen { Cliente = "15191", Contacto = "1", Direccion = "CALLE MENOR, 5" },
                new FilaPackingAlmacen { Cliente = "20000", Contacto = "0", Direccion = "AVDA. SOL, 9" }
            };

            DireccionCompletaCliente.Rellenar(filas, Filas(("15191", "0", "LOCAL · RÓTULO: LOHÉ"), ("20000", "0", "")),
                f => f.Cliente, f => f.Contacto, f => f.Direccion, (f, d) => f.Direccion = d);

            Assert.AreEqual("CALLE MAYOR, 3, LOCAL · RÓTULO: LOHÉ", filas[0].Direccion);
            Assert.AreEqual("CALLE MENOR, 5", filas[1].Direccion);
            Assert.AreEqual("AVDA. SOL, 9", filas[2].Direccion);
        }

        // ---- Packing en PDF (prdInformePicking, sin tocar el SP) ----

        [TestMethod]
        public void PackingPdf_CompletaPorEmpresaDelPedido_UnaLecturaPorEmpresa()
        {
            var lista = new List<PackingDTO>
            {
                new PackingDTO { Número = 1, NºCliente = "15191", Contacto = "0", Direccion = "CALLE MAYOR, 3" },
                new PackingDTO { Número = 1, NºCliente = "15191", Contacto = "0", Direccion = "CALLE MAYOR, 3" },
                new PackingDTO { Número = 2, NºCliente = "15191", Contacto = "0", Direccion = "CALLE MAYOR, 3" },
                new PackingDTO { Número = 3, NºCliente = "30000", Contacto = "0", Direccion = "CALLE TRES" }
            };
            var lecturas = new List<string>();

            InformesService.CompletarDireccion2Packing(lista, p => p == 2 ? "3" : p == 1 ? "1" : null, (empresa, clientes) =>
            {
                lecturas.Add(empresa);
                return empresa == "1" ? Filas(("15191", "0", "1º D")) : null; // la 3 sin filas (o sin columnas)
            });

            Assert.AreEqual("CALLE MAYOR, 3, 1º D", lista[0].Direccion);
            Assert.AreEqual("CALLE MAYOR, 3, 1º D", lista[1].Direccion);
            Assert.AreEqual("CALLE MAYOR, 3", lista[2].Direccion, "pedido de la empresa 3: su ficha no tiene Dirección 2");
            Assert.AreEqual("CALLE TRES", lista[3].Direccion, "sin empresa conocida = la de por defecto, sin fila");
            CollectionAssert.AreEquivalent(new[] { "1", "3" }, lecturas);
        }

        // ---- Correo del pedido / presupuesto ----

        [TestMethod]
        public void CorreoPedido_DireccionDeEntregaConDireccion2()
        {
            Direccion2ClienteSql.LectorParaTests = (e, c, k) => c == "15191" && k == "0" ? "NAVE 4" : null;
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "15191", contacto = "0" };

            Assert.AreEqual("POL. IND. 2, NAVE 4", GestorPresupuestos.DireccionEntregaCorreo(null, pedido, new Cliente { Dirección = "POL. IND. 2   " }));
            pedido.contacto = "1";
            Assert.AreEqual("POL. IND. 2", GestorPresupuestos.DireccionEntregaCorreo(null, pedido, new Cliente { Dirección = "POL. IND. 2   " }));
        }

        // ---- Documentos: dirección de ENTREGA sí, FISCAL no ----

        private static IServicioFacturas ServicioConPedido(string direccion2)
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            var cab = new CabPedidoVta { Empresa = "1", Número = 900001, Nº_Cliente = "15191", Contacto = "0", Serie = "NV", Vendedor = "VD", Fecha = new DateTime(2026, 11, 19) };
            cab.LinPedidoVtas.Add(new LinPedidoVta
            {
                Empresa = "1", Número = 900001, Cantidad = 1, Texto = "PRODUCTO", Precio = 10, Producto = "12345", Base_Imponible = 10M,
                ImporteIVA = 2.1M, Total = 12.1M, PorcentajeIVA = 21, TipoLinea = 1, Contacto = "0"
            });
            var cliente = new Cliente
            {
                Empresa = "1", Nº_Cliente = "15191", Contacto = "0", Nombre = "LOHÉ", CIF_NIF = "B11111111", Dirección = "CALLE MAYOR, 3",
                CodPostal = "28001", Población = "MADRID", Provincia = "MADRID", ClientePrincipal = true, AlbaranValorado = true
            };
            A.CallTo(() => servicio.CargarCabPedido("1", 900001)).Returns(cab);
            A.CallTo(() => servicio.CargarCliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(cliente);
            A.CallTo(() => servicio.CargarClientePrincipal(A<string>.Ignored, A<string>.Ignored)).Returns(cliente);
            A.CallTo(() => servicio.LeerDireccion2Cliente("1", "15191", "0")).Returns(direccion2);
            A.CallTo(() => servicio.CargarEfectosPedido("1", 900001)).Returns(new List<EfectoPedidoVenta>
            {
                new EfectoPedidoVenta { Empresa = "1", Pedido = 900001, Importe = 12.1M, FormaPago = "EFC", FechaVencimiento = new DateTime(2026, 11, 19) }
            });
            return servicio;
        }

        [TestMethod]
        public void PdfPedido_EntregaConDireccion2_FiscalSinElla()
        {
            IServicioFacturas servicio = ServicioConPedido("LOCAL · RÓTULO: LOHÉ");

            Factura documento = new GestorFacturas(servicio).LeerPedido("1", 900001);

            Assert.AreEqual("CALLE MAYOR, 3, LOCAL · RÓTULO: LOHÉ", documento.Direcciones.Single(d => d.Tipo == "Entrega").Direccion);
            Assert.AreEqual("CALLE MAYOR, 3", documento.Direcciones.Single(d => d.Tipo == "Fiscal").Direccion);
        }

        [TestMethod]
        public void PdfPedido_SinDireccion2OSiFallaLaLectura_ComoSiempre()
        {
            IServicioFacturas servicio = ServicioConPedido(null);
            Assert.AreEqual("CALLE MAYOR, 3", new GestorFacturas(servicio).LeerPedido("1", 900001).Direcciones.Single(d => d.Tipo == "Entrega").Direccion);

            A.CallTo(() => servicio.LeerDireccion2Cliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Throws(new Exception("sin conexión"));
            Assert.AreEqual("CALLE MAYOR, 3", new GestorFacturas(servicio).LeerPedido("1", 900001).Direcciones.Single(d => d.Tipo == "Entrega").Direccion);
        }
    }
}
