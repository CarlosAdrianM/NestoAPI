using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models.Comisiones;
using NestoAPI.Models.Comisiones.Estetica;

namespace NestoAPI.Tests.Models.Comisiones
{
    [TestClass]
    public class CalculadorProyecciones2019Test
    {
        const string GENERAL = "General";        

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_LasVentasEjemploDelAnnoAnteriorFueron136000()
        {
            ICollection<ResumenComisionesMes> ventasAnnoPasado = CrearVentasAnnoPasado();
            
            decimal venta = ventasAnnoPasado.Sum(r => (r.Etiquetas.Where(e => e.Nombre == GENERAL).Single() as IEtiquetaComisionVenta).Venta);

            Assert.AreEqual(136000, venta);
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_LasVentasEjemploDeEneroFueron11000()
        {
            ICollection<ResumenComisionesMes> ventasAnnoActual = CrearVentasAnnoActual();

            decimal venta = ventasAnnoActual.Where(v => v.Anno == 2019 && v.Mes <= 1).Sum(r => (r.Etiquetas.Where(e => e.Nombre == GENERAL).Single() as IEtiquetaComisionVenta).Venta);

            Assert.AreEqual(11000, venta);
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_LaProyeccionEnEneroEsDe137000()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(CrearVentasAnnoActual());
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2018, true)).Returns(CrearVentasAnnoPasado());

            var proyeccion = calculador.CalcularProyeccion("VD", 2019, 1, 0, 0, 0);

            Assert.AreEqual(137000, proyeccion);
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_LaProyeccionEnFebreroEsDe138600()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(CrearVentasAnnoActual());
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2018, true)).Returns(CrearVentasAnnoPasado());

            var proyeccion = calculador.CalcularProyeccion("VD", 2019, 2, 0, 0, 0);

            Assert.AreEqual(138600, proyeccion);
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_LaProyeccionEnDiciembreEsDe138600()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(CrearVentasAnnoActual());
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2018, true)).Returns(CrearVentasAnnoPasado());

            var proyeccion = calculador.CalcularProyeccion("VD", 2019, 12, 0, 0, 0);

            Assert.AreEqual(138600, proyeccion);
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_SiFaltaUnMesSeRellenaConLaMedia()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(CrearVentasAnnoActual());
            var ventasAnnoPasado = CrearVentasAnnoPasado();
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            // quedan 92000 de venta de mayo en adelante
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2018, true)).Returns(ventasAnnoPasado);

            var proyeccion = calculador.CalcularProyeccion("VD", 2019, 3, 0, 0, 0);

            Assert.AreEqual(138133.33M, proyeccion); //(34600/3)*4+92000
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_SiFaltanVariosMesesSeRellenanConLaMedia()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(CrearVentasAnnoActual());
            var ventasAnnoPasado = CrearVentasAnnoPasado();
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            // quedan 92000 de venta de mayo en adelante
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2018, true)).Returns(ventasAnnoPasado);

            var proyeccion = calculador.CalcularProyeccion("VD", 2019, 1, 0, 0, 0);

            Assert.AreEqual(136000, proyeccion); //11000*4+92000
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_SiElAnnoActualNoComienzaEnEneroSeHaceLaParteProporcional()
        {
            // Como leemos solo a septiembre y faltan 4 meses, por eso es 11000 x 4
            // Para el futuro sería bueno refactorizarlo para elevarlo al año
            // 11000 x 11 = 121000 (si lo que sí tenemos incluye agosto, entonces x12)
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            var ventasAnnoActual = CrearVentasAnnoActual();
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(ventasAnnoActual);

            var proyeccion = calculador.CalcularProyeccion("VD", 2019, 9, 0, 0, 0);

            Assert.AreEqual(44000, proyeccion); //11000 * 4
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_SiSeHaceLaParteProporcionalAntesDeAgostoEsPor11()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            var ventasAnnoActual = CrearVentasAnnoActual();
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            ventasAnnoActual.Remove(ventasAnnoActual.ElementAt(0));
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(ventasAnnoActual);

            var proyeccion = calculador.CalcularProyeccion("VD", 2019, 6, 0, 0, 0);

            Assert.AreEqual(84000, proyeccion); //14000 * 6
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_SiSeHaceLaParteProporcionalAntesDeAgostoEsPor11AunqueElAnnoPasadoEmpieceDespuesDeAgosto()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            var ventasAnnoActual = CrearVentasAnnoActual();
            // las ventas de enero a junio son 74600 (media de 12433.33)
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(ventasAnnoActual);

            var ventasAnnoPasado = CrearVentasAnnoPasado();
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            ventasAnnoPasado.Remove(ventasAnnoPasado.ElementAt(0));
            // quedan 10000 de venta de diciembre
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2018, true)).Returns(ventasAnnoPasado);

            var proyeccion = calculador.CalcularProyeccion("VD", 2019, 6, 0, 0, 0);

            Assert.AreEqual(134333.33M, proyeccion); //74600 que lleva a junio + 12433.33 * 4 (de julio a noviembre sin agosto) = 49733.32 + 10000 de diciembre
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_SiLaVentaDelMesSiguienteEsMayorQueLoAcumuladoBajaDeSalto()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(CrearVentasAnnoActual());
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2018, true)).Returns(CrearVentasAnnoPasado());
            var tramos = new List<TramoComision> {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 136000M,
                    Tipo = 0.1M,
                    TipoExtra = 0M
                },
                new TramoComision
                {
                    Desde = 136000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = 0.2M,
                    TipoExtra = 0.1M
                }
            };

            var ventasMes = 17000M;
            var resumen = CrearVentasAnnoActual().First();
            resumen.GeneralProyeccion = calculador.CalcularProyeccion("VD", 2019, 1, ventasMes, 0, 0);

            bool baja = calculador.CalcularSiBajaDeSalto("VD", 2019, 1, 0, resumen, ventasMes, 0, tramos);


            Assert.IsTrue(baja);
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_SiLaVentaDelMesSiguienteEsMenorQueLoAcumuladoNoBajaDeSalto()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(CrearVentasAnnoActual());
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2018, true)).Returns(CrearVentasAnnoPasado());
            var tramos = new List<TramoComision> {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 136000M,
                    Tipo = 0.1M,
                    TipoExtra = 0M
                },
                new TramoComision
                {
                    Desde = 136000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = 0.2M,
                    TipoExtra = 0.1M
                }
            };

            var ventasMes = 27000M;
            var resumen = CrearVentasAnnoActual().First();
            resumen.GeneralProyeccion = calculador.CalcularProyeccion("VD", 2019, 1, ventasMes, 0, 0);

            bool baja = calculador.CalcularSiBajaDeSalto("VD", 2019, 1, 0, resumen, ventasMes, 0, tramos);

            Assert.IsFalse(baja);
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_SiLaVentaDelMesSiguienteNoEstaRegistradaCalculaLaMedia()
        {
            var comisiones = A.Fake<IComisionesAnuales>();
            var calculador = new CalculadorProyecciones2019(comisiones);
            //A.CallTo(() => servicio.CalculadorProyecciones).Returns(calculador);
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2019, true)).Returns(CrearVentasAnnoActual());
            var ventasAnnoPasado = CrearVentasAnnoPasado();
            var tramoFebrero = ventasAnnoPasado.Where(v => v.Mes == 2).Single();
            ventasAnnoPasado.Remove(tramoFebrero);
            A.CallTo(() => comisiones.LeerResumenAnno("VD", 2018, true)).Returns(ventasAnnoPasado);
            var tramos = new List<TramoComision> {
                new TramoComision
                {
                    Desde = 0M,
                    Hasta = 136000M,
                    Tipo = 0.1M,
                    TipoExtra = 0M
                },
                new TramoComision
                {
                    Desde = 136000.01M,
                    Hasta = decimal.MaxValue,
                    Tipo = 0.2M,
                    TipoExtra = 0.1M
                }
            };

            var ventasMes = 17000M;
            var resumen = CrearVentasAnnoActual().First();
            resumen.GeneralProyeccion = calculador.CalcularProyeccion("VD", 2019, 1, ventasMes, 1, 1);

            bool baja = calculador.CalcularSiBajaDeSalto("VD", 2019, 1, 0, resumen, ventasMes, 0, tramos);
            
            Assert.IsTrue(baja);
        }

        [TestMethod]
        public void CalculadorComisiones2019_CalcularProyeccion_SiEsJefeDeVentasSumaLasProyeccionesDeTodoSuEquipo()
        {            
            var servicio = A.Fake<IServicioComisionesAnuales>();
            var comisiones = new ComisionesAnualesEstetica2024(servicio);



        }
               

        // Datos Fake
        private ICollection<ResumenComisionesMes> CrearVentasAnnoActual()
        {
            IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
            ResumenComisionesMes enero = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 1,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 11000
                    }
                }
            };
            ResumenComisionesMes febrero = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 2,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 11600
                    }
                }
            };
            ResumenComisionesMes marzo = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 3,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 12000
                    }
                }
            };
            ResumenComisionesMes abril = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 4,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 12000
                    }
                }
            };
            ResumenComisionesMes mayo = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 5,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 14000
                    }
                }
            };
            ResumenComisionesMes junio = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 6,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 14000
                    }
                }
            };
            ResumenComisionesMes julio = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 7,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 15000
                    }
                }
            };
            ResumenComisionesMes agosto = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 8,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 5000
                    }
                }
            };
            ResumenComisionesMes septiembre = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 9,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 11000
                    }
                }
            };
            ResumenComisionesMes octubre = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 10,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 11000
                    }
                }
            };
            ResumenComisionesMes noviembre = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 11,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 12000
                    }
                }
            };
            ResumenComisionesMes diciembre = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2019,
                Mes = 12,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 10000
                    }
                }
            };
            var lista = new List<ResumenComisionesMes> {
                enero,
                febrero,
                marzo,
                abril,
                mayo,
                junio,
                julio,
                agosto,
                septiembre,
                octubre,
                noviembre,
                diciembre
            };

            return lista;
        }

        private ICollection<ResumenComisionesMes> CrearVentasAnnoPasado()
        {
            IServicioComisionesAnuales servicio = A.Fake<IServicioComisionesAnuales>();
            ResumenComisionesMes enero = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 1,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 10000
                    }
                }
            };
            ResumenComisionesMes febrero = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 2,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 10000
                    }
                }
            };
            ResumenComisionesMes marzo = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 3,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 12000
                    }
                }
            };
            ResumenComisionesMes abril = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 4,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 12000
                    }
                }
            };
            ResumenComisionesMes mayo = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 5,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 14000
                    }
                }
            };
            ResumenComisionesMes junio = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 6,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 14000
                    }
                }
            };
            ResumenComisionesMes julio = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 7,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 15000
                    }
                }
            };
            ResumenComisionesMes agosto = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 8,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 5000
                    }
                }
            };
            ResumenComisionesMes septiembre = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 9,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 11000
                    }
                }
            };
            ResumenComisionesMes octubre = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 10,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 11000
                    }
                }
            };
            ResumenComisionesMes noviembre = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 11,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 12000
                    }
                }
            };
            ResumenComisionesMes diciembre = new ResumenComisionesMes
            {
                Vendedor = "VD",
                Anno = 2018,
                Mes = 12,
                Etiquetas = new List<IEtiquetaComision>
                {
                    new EtiquetaGeneral(servicio)
                    {
                         Venta = 10000
                    }
                }
            };
            var lista = new List<ResumenComisionesMes> {
                enero,
                febrero,
                marzo,
                abril,
                mayo,
                junio,
                julio,
                agosto,
                septiembre,
                octubre,
                noviembre,
                diciembre
            };

            return lista;
        }

    }
}
