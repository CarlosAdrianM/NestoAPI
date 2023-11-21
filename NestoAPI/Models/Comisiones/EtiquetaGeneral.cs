﻿using NestoAPI.Infraestructure.Vendedores;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Estetica
{
    public class EtiquetaGeneral : IEtiquetaComision
    {
        private NVEntities db = new NVEntities();

        IQueryable<vstLinPedidoVtaComisione> consulta;

        public EtiquetaGeneral()
        {

        }

        public string Nombre {
            get {
                return "General";
            }
        }

        public decimal Venta { get; set; }
        public decimal Tipo { get; set; }
        public decimal Comision { get; set; }
        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            return LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, false);
        }
        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);
            CrearConsulta();
            var servicioVendedores = new ServicioVendedores();
            var listaVendedores = (servicioVendedores.VendedoresEquipo(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor).GetAwaiter().GetResult()).Select(v => v.vendedor);
            consulta = consulta
                .Where(l =>
                    listaVendedores.Contains(l.Vendedor)
                );

            return ServicioComisionesAnualesComun.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        public decimal SetTipo(TramoComision tramo)
        {
            return tramo.Tipo;
        }

        private void CrearConsulta()
        {
            consulta = db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.EstadoFamilia == 0 &&
                    l.Familia.ToLower() != "unionlaser" &&
                    l.Grupo.ToLower() != "otros aparatos"
                );
        }

        IQueryable<vstLinPedidoVtaComisione> IEtiquetaComision.LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);
            CrearConsulta();
            var servicioVendedores = new ServicioVendedores();
            var listaVendedores = (servicioVendedores.VendedoresEquipo(Constantes.Empresas.EMPRESA_POR_DEFECTO, vendedor).GetAwaiter().GetResult()).Select(v => v.vendedor);
            consulta = consulta
                .Where(l =>
                    listaVendedores.Contains(l.Vendedor)
                );

            return ServicioComisionesAnualesComun.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }
    }
}