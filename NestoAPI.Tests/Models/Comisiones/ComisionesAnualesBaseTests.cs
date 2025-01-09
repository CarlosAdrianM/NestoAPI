using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Models;
using NestoAPI.Models.Comisiones;
using System;
using System.Collections.Generic;
using System.Linq;
using static NestoAPI.Models.Constantes;

namespace NestoAPI.Tests.Models.Comisiones
{
    [TestClass]
    public class ComisionesAnualesBaseTests
    {
        [TestMethod]
        public void ComisionesAnualesBase_LeerResumenAnno_SiLaEtiquetaEsDeComisionAcumuladaSumaLaComision()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            List<string> listaVendedor = new List<string> { "VD" };
            A.CallTo(() => servicio.ListaVendedores("VD")).Returns(listaVendedor);
            A.CallTo(() => servicio.LeerComisionesAnualesResumenMes(listaVendedor, 2024)).Returns(new List<ComisionAnualResumenMes>
            {
                new ComisionAnualResumenMes
                {
                    Etiqueta = "Test Acumulada",
                    Venta = 10M, // al 10% debería dar 1 €
                    Comision = 4M, // Esto es lo que tiene que devolver: 4€
                    Tipo = 0.1M
                }
            });
            var sut = new ComisionesAnualesTest(servicio);
            sut.ListaEtiquetas = new List<IEtiquetaComision> { new EtiquetaTestAcumulada() };

            // Act
            var resultado = sut.LeerResumenAnno("VD", 2024);

            // Assert
            Assert.AreEqual(4M, resultado.Single().Etiquetas.Single().Comision);
        }

        [TestMethod]
        public void ComisionesAnualesBase_LeerResumenAnno_SiLaEtiquetaNoEsDeComisionAcumuladaNoSumaLaComision()
        {
            // Arrange
            var servicio = A.Fake<IServicioComisionesAnuales>();
            List<string> listaVendedor = new List<string> { "VD" };
            A.CallTo(() => servicio.ListaVendedores("VD")).Returns(listaVendedor);
            A.CallTo(() => servicio.LeerComisionesAnualesResumenMes(listaVendedor, 2024)).Returns(new List<ComisionAnualResumenMes>
            {
                new ComisionAnualResumenMes
                {
                    Etiqueta = "Test Anual o No Acumulada",
                    Venta = 10M, // Esto es lo que tiene que devolver: al 10% debe dar 1 €
                    Comision = 4M, // A esto no le tiene que hacer ni caso
                    Tipo = 0.1M
                }
            });
            var sut = new ComisionesAnualesTest(servicio);
            sut.ListaEtiquetas = new List<IEtiquetaComision> { new EtiquetaTestAnual() };

            // Act
            var resultado = sut.LeerResumenAnno("VD", 2024);

            // Assert
            Assert.AreEqual(1M, resultado.Single().Etiquetas.Single().Comision);
        }
    }




    public class ComisionesAnualesTest : ComisionesAnualesBase, IComisionesAnuales
    {
        public List<IEtiquetaComision> ListaEtiquetas { get; set; }
        public override ICollection<IEtiquetaComision> NuevasEtiquetas => ListaEtiquetas;
        public ICalculadorProyecciones CalculadorProyecciones => throw new NotImplementedException();

        public ComisionesAnualesTest(IServicioComisionesAnuales servicio)
            :base(servicio) { }

        public ICollection<TramoComision> LeerTramosComisionMes(string vendedor)
        {
            return new List<TramoComision>
            {
                new TramoComision
                {
                    Desde = 0,
                    Hasta = 1000,
                    Tipo = 0.10M
                }
            };
        }

        public ICollection<TramoComision> LeerTramosComisionAnno(string vendedor)
        {
            throw new NotImplementedException();
        }

        public string EtiquetaLinea(vstLinPedidoVtaComisione linea)
        {
            throw new NotImplementedException();
        }
    }
    public class EtiquetaTestAcumulada : IEtiquetaComisionVenta
    {
        public string Nombre => "Test Acumulada";

        public decimal Tipo { get; set; }
        public decimal Comision { get; set; }

        public bool EsComisionAcumulada => true;

        public bool SoloExisteDatoAnual => false;

        public decimal Venta { get; set; }

        public object Clone()
        {
            return new EtiquetaTestAcumulada
            {
                Comision = this.Comision,
                Tipo = this.Tipo,
                Venta = this.Venta
            };
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            throw new NotImplementedException();
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            throw new NotImplementedException();
        }

        public IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            throw new NotImplementedException();
        }

        public bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea)
        {
            throw new NotImplementedException();
        }

        public decimal SetTipo(TramoComision tramo)
        {
            return tramo.Tipo;
        }
    }
    public class EtiquetaTestAnual : IEtiquetaComisionVenta
    {
        public string Nombre => "Test Anual o No Acumulada";

        public decimal Tipo { get; set; }
        public decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2);
            set => throw new Exception("La comisión de este test no se puede fijar manualmente");
        }

        public bool EsComisionAcumulada => false;

        public bool SoloExisteDatoAnual => true;

        public decimal Venta { get; set; }

        public object Clone()
        {
            return new EtiquetaTestAnual
            {
                Tipo = this.Tipo,
                Venta = this.Venta
            };
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            throw new NotImplementedException();
        }

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            throw new NotImplementedException();
        }

        public IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            throw new NotImplementedException();
        }

        public bool PerteneceALaEtiqueta(vstLinPedidoVtaComisione linea)
        {
            throw new NotImplementedException();
        }

        public decimal SetTipo(TramoComision tramo)
        {
            return tramo.Tipo;
        }
    }
}
