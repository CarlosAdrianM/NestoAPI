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

        // ---- Bloque 2: listas de pantalla (pedidos, clientes nuevos, facturación de tienda) ----

        [TestMethod]
        public void ListaPedidos_DireccionCompleta_UnaLecturaPorEmpresa_YSinColumnasLaDireccion()
        {
            var lecturas = new List<string>();
            Direccion2ClienteSql.LectorClientesParaTests = (empresa, clientes) =>
            {
                lecturas.Add(empresa + ":" + string.Join(",", clientes));
                return empresa == "1" ? Filas(("15191", "0", "1º D")) : null;
            };
            var pedidos = new List<ResumenPedidoVentaDTO>
            {
                new ResumenPedidoVentaDTO { empresa = "1", numero = 1, cliente = "15191", contacto = "0", direccion = "CALLE MAYOR, 3" },
                new ResumenPedidoVentaDTO { empresa = "1", numero = 2, cliente = "15191", contacto = "0", direccion = "CALLE MAYOR, 3" },
                new ResumenPedidoVentaDTO { empresa = "1", numero = 3, cliente = "20000", contacto = "0", direccion = "AVDA. SOL, 9" },
                new ResumenPedidoVentaDTO { empresa = "3", numero = 4, cliente = "15191", contacto = "0", direccion = "CALLE MAYOR, 3" }
            };

            DireccionCompletaCliente.RellenarPorEmpresa(null, pedidos, p => p.empresa, p => p.cliente, p => p.contacto, p => p.direccion,
                (p, d) => p.direccionCompleta = d);

            CollectionAssert.AreEquivalent(new[] { "1:15191,20000", "3:15191" }, lecturas, "una por empresa, clientes sin repetir");
            Assert.AreEqual("CALLE MAYOR, 3, 1º D", pedidos[0].direccionCompleta);
            Assert.AreEqual("CALLE MAYOR, 3, 1º D", pedidos[1].direccionCompleta);
            Assert.AreEqual("AVDA. SOL, 9", pedidos[2].direccionCompleta);
            Assert.AreEqual("CALLE MAYOR, 3", pedidos[3].direccionCompleta);
            Assert.AreEqual("CALLE MAYOR, 3", pedidos[0].direccion, "direccion no cambia");
        }

        [TestMethod]
        public void ClientesNuevos_CorreoConDireccion2_YSinFilasComoEstaba()
        {
            var resultado = new Dictionary<string, List<ClienteNuevoVendedorDTO>>
            {
                ["JE"] = new List<ClienteNuevoVendedorDTO> { new ClienteNuevoVendedorDTO { Cliente = "15191", Contacto = "0", Direccion = "CALLE MAYOR, 3" } },
                ["NV"] = new List<ClienteNuevoVendedorDTO> { new ClienteNuevoVendedorDTO { Cliente = "15191", Contacto = "0", Direccion = "CALLE MAYOR, 3" } }
            };

            InformeClientesNuevosJobsService.CompletarDireccion2(resultado, Filas(("15191", "0", "NAVE 4")));

            Assert.AreEqual("CALLE MAYOR, 3, NAVE 4", resultado["JE"][0].Direccion);
            Assert.AreEqual("CALLE MAYOR, 3, NAVE 4", resultado["NV"][0].Direccion);

            var sin = new Dictionary<string, List<ClienteNuevoVendedorDTO>>
            {
                ["JE"] = new List<ClienteNuevoVendedorDTO> { new ClienteNuevoVendedorDTO { Cliente = "15191", Contacto = "0", Direccion = "CALLE MAYOR, 3" } }
            };
            InformeClientesNuevosJobsService.CompletarDireccion2(sin, null);
            Assert.AreEqual("CALLE MAYOR, 3", sin["JE"][0].Direccion);
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
        public void PdfPedido_EntregaYFiscalConDireccion2()
        {
            // Bloque 4a (Carlos 19/11/26): un pedido no está emitido; su fiscal (de la ficha) también la lleva.
            IServicioFacturas servicio = ServicioConPedido("LOCAL · RÓTULO: LOHÉ");

            Factura documento = new GestorFacturas(servicio).LeerPedido("1", 900001);

            Assert.AreEqual("CALLE MAYOR, 3, LOCAL · RÓTULO: LOHÉ", documento.Direcciones.Single(d => d.Tipo == "Entrega").Direccion);
            Assert.AreEqual("CALLE MAYOR, 3, LOCAL · RÓTULO: LOHÉ", documento.Direcciones.Single(d => d.Tipo == "Fiscal").Direccion);
        }

        // ---- Bloque 4a: dirección fiscal ----

        [TestMethod]
        public void ParaFiscal_LaCompletaSiCabeEntera_SiNoLaDireccionSola()
        {
            Assert.AreEqual("CALLE MAYOR, 3, 1º D", DireccionCompletaCliente.ParaFiscal("CALLE MAYOR, 3  ", "1º D"));
            Assert.AreEqual("CALLE MAYOR, 3", DireccionCompletaCliente.ParaFiscal("CALLE MAYOR, 3", null));
            string calle = new string('A', 150);
            string d2 = new string('B', 60); // 150 + 2 + 60 = 212 > 200
            Assert.AreEqual(calle, DireccionCompletaCliente.ParaFiscal(calle, d2), "no se trunca: la Dirección sola");
            Assert.AreEqual(calle + ", " + new string('B', 48), DireccionCompletaCliente.ParaFiscal(calle, new string('B', 48)), "justo 200 sí cabe");
            Assert.AreEqual(200, DireccionCompletaCliente.LONGITUD_DIRECCION_FISCAL);
        }

        private static (IServicioFacturas servicio, CabFacturaVta cab) ServicioConFactura(string direccionFiscalGuardada)
        {
            IServicioFacturas servicio = A.Fake<IServicioFacturas>();
            CabFacturaVta cab = A.Fake<CabFacturaVta>();
            cab.Vendedor = "VD";
            cab.Nº_Cliente = "15191";
            cab.Serie = "NV";
            cab.NombreFiscal = direccionFiscalGuardada == null ? null : "LOHÉ S.L.";
            cab.CifNif = direccionFiscalGuardada == null ? null : "B11111111";
            cab.DireccionFiscal = direccionFiscalGuardada;
            cab.LinPedidoVtas.Add(new LinPedidoVta
            {
                Nº_Albarán = 1, Fecha_Albarán = new DateTime(2026, 1, 29), Cantidad = 1, Texto = "PRODUCTO", Precio = 100, Producto = "12345",
                Base_Imponible = 100M, ImporteIVA = 21M, ImporteRE = 0, Total = 121, PorcentajeIVA = 21, PorcentajeRE = 0M
            });
            A.CallTo(() => servicio.CargarCabFactura("1", "NV26/000001")).Returns(cab);
            var cliente = new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0", Nombre = "LOHÉ S.L.", CIF_NIF = "B11111111", Dirección = "CALLE MAYOR, 3", ClientePrincipal = true };
            A.CallTo(() => servicio.CargarCliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns(cliente);
            A.CallTo(() => servicio.CargarClientePrincipal(A<string>.Ignored, A<string>.Ignored)).Returns(cliente);
            A.CallTo(() => servicio.LeerDireccion2Cliente(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Returns("1º D");
            A.CallTo(() => servicio.CargarVencimientosExtracto(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(new List<VencimientoFactura> { new VencimientoFactura { Importe = 121, ImportePendiente = 121 } });
            return (servicio, cab);
        }

        [TestMethod]
        public void FacturaYaEmitida_ConDatosGuardados_LaFiscalNoCambia_AunqueLaFichaTengaDireccion2()
        {
            (IServicioFacturas servicio, _) = ServicioConFactura("CALLE MAYOR, 3");

            Factura factura = new GestorFacturas(servicio).LeerFactura("1", "NV26/000001");

            Assert.AreEqual("CALLE MAYOR, 3", factura.Direcciones.Single(d => d.Tipo == "Fiscal").Direccion);
            Assert.AreEqual("CALLE MAYOR, 3, 1º D", factura.Direcciones.Single(d => d.Tipo == "Entrega").Direccion, "la de entrega sí (bloque 1)");
        }

        [TestMethod]
        public void FacturaAntigua_SinDatosGuardados_LaFiscalDeLaFichaSinDireccion2()
        {
            (IServicioFacturas servicio, _) = ServicioConFactura(null);

            Factura factura = new GestorFacturas(servicio).LeerFactura("1", "NV26/000001");

            Assert.AreEqual("CALLE MAYOR, 3", factura.Direcciones.Single(d => d.Tipo == "Fiscal").Direccion);
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
