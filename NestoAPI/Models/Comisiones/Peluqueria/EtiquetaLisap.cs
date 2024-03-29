﻿using System;
using System.Linq;

namespace NestoAPI.Models.Comisiones.Peluqueria
{
    public class EtiquetaLisap : IEtiquetaComisionVenta
    {
        private IQueryable<vstLinPedidoVtaComisione> consulta;
        private readonly IServicioComisionesAnuales _servicioComisiones;

        public EtiquetaLisap(IServicioComisionesAnuales servicioComisiones)
        {
            this._servicioComisiones = servicioComisiones;
        }

        public string Nombre => "Lisap";

        public decimal Venta { get; set; }
        public decimal Tipo { get; set; }
        public decimal Comision
        {
            get => Math.Round(Venta * Tipo, 2);
            set => throw new Exception("La comisión de Lisap no se puede fijar manualmente");
        }
        public bool EsComisionAcumulada => false;

        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes)
        {
            return LeerVentaMes(vendedor, anno, mes, incluirAlbaranes, false);
        }
        public decimal LeerVentaMes(string vendedor, int anno, int mes, bool incluirAlbaranes, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (fechaDesde < new DateTime(2018, 4, 1))
            {
                throw new Exception("Las comisiones anuales de peluquería entraron en vigor el 01/04/18");
            }

            CrearConsulta(vendedor);

            return _servicioComisiones.CalcularVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        public IQueryable<vstLinPedidoVtaComisione> LeerVentaMesDetalle(string vendedor, int anno, int mes, bool incluirAlbaranes, string etiqueta, bool incluirPicking)
        {
            DateTime fechaDesde = VendedorComisionAnual.FechaDesde(anno, mes);
            DateTime fechaHasta = VendedorComisionAnual.FechaHasta(anno, mes);

            if (consulta == null)
            {
                CrearConsulta(vendedor);
            }

            return _servicioComisiones.ConsultaVentaFiltrada(incluirAlbaranes, fechaDesde, fechaHasta, ref consulta, incluirPicking);
        }

        private void CrearConsulta(string vendedor)
        {
            consulta = _servicioComisiones.Db.vstLinPedidoVtaComisiones
                .Where(l =>
                    l.Familia.ToLower() == "lisap" &&
                    l.Vendedor == vendedor
                );
        }

        public decimal SetTipo(TramoComision tramo) => tramo.TipoExtra;

        public object Clone() => new EtiquetaLisap(_servicioComisiones)
        {
            Venta = this.Venta,
            Tipo = this.Tipo
        };
    }
}