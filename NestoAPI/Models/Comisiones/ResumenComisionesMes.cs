using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Comisiones
{
    public class ResumenComisionesMes
    {
        public ResumenComisionesMes()
        {
            Etiquetas = new List<IEtiquetaComision>();
        }

        public string Vendedor { get; set; }

        public int Anno { get; set; }
        public int Mes { get; set; }

        public ICollection<IEtiquetaComision> Etiquetas { get; set; }
        private IEtiquetaComisionAcumulada EtiquetaGeneralAcumulada => ComisionesHelper.ObtenerEtiquetaAcumulada(Etiquetas);

        private void SetEtiquetaGeneralProperty(Action<IEtiquetaComisionAcumulada> setter)
        {
            var etiqueta = EtiquetaGeneralAcumulada;
            if (etiqueta != null)
            {
                setter(etiqueta);
            }
        }


        [Obsolete("Use la propiedad FaltaParaSalto de la etiqueta General")]
        public decimal GeneralFaltaParaSalto
        {
            get => EtiquetaGeneralAcumulada?.FaltaParaSalto ?? 0;
            set => SetEtiquetaGeneralProperty(e => e.FaltaParaSalto = value);
        }

        [Obsolete("Use la propiedad InicioTramo de la etiqueta General")]
        public decimal GeneralInicioTramo
        {
            get => EtiquetaGeneralAcumulada?.InicioTramo ?? 0;
            set => SetEtiquetaGeneralProperty(e => e.InicioTramo = value);
        }

        [Obsolete("Use la propiedad FinalTramo de la etiqueta General")]
        public decimal GeneralFinalTramo
        {
            get => EtiquetaGeneralAcumulada?.FinalTramo ?? 0;
            set => SetEtiquetaGeneralProperty(e => e.FinalTramo = value);
        }

        [Obsolete("Use la propiedad BajaSaltoMesSiguiente de la etiqueta General")]
        public bool GeneralBajaSaltoMesSiguiente
        {
            get => EtiquetaGeneralAcumulada?.BajaSaltoMesSiguiente ?? false;
            set => SetEtiquetaGeneralProperty(e => e.BajaSaltoMesSiguiente = value);
        }

        [Obsolete("Use la propiedad Proyeccion de la etiqueta General")]
        public decimal GeneralProyeccion
        {
            get => EtiquetaGeneralAcumulada?.Proyeccion ?? 0;
            set => SetEtiquetaGeneralProperty(e => e.Proyeccion = value);
        }

        [Obsolete("Use la propiedad VentaAcumulada de la etiqueta General")]
        public decimal GeneralVentaAcumulada
        {
            get => EtiquetaGeneralAcumulada?.VentaAcumulada ?? 0;
            set => SetEtiquetaGeneralProperty(e => e.VentaAcumulada = value);
        }

        [Obsolete("Use la propiedad ComisionAcumulada de la etiqueta General")]
        public decimal GeneralComisionAcumulada
        {
            get => EtiquetaGeneralAcumulada?.ComisionAcumulada ?? 0;
            set => SetEtiquetaGeneralProperty(e => e.ComisionAcumulada = value);
        }

        [Obsolete("Use la propiedad TipoConseguido de la etiqueta General")]
        public decimal GeneralTipoConseguido
        {
            get => EtiquetaGeneralAcumulada?.TipoConseguido ?? 0;
            set => SetEtiquetaGeneralProperty(e => e.TipoConseguido = value);
        }

        [Obsolete("Use la propiedad TipoReal de la etiqueta General")]
        public decimal GeneralTipoReal => EtiquetaGeneralAcumulada?.TipoReal ?? 0;

        public decimal TotalComisiones => Math.Round(Etiquetas.Sum(e => e.Comision), 2);

        public decimal TotalVentaAcumulada { get; set; }
        public decimal TotalComisionAcumulada { get; set; }
        public decimal TotalTipoAcumulado => TotalVentaAcumulada == 0 ? 0 : Math.Round(TotalComisionAcumulada / TotalVentaAcumulada, 4, MidpointRounding.AwayFromZero);


        // Estrategia de compensación de sobrepago
        [Obsolete("Use la propiedad EstrategiaUtilizada de la etiqueta General")]
        public string EstrategiaUtilizada
        {
            get => EtiquetaGeneralAcumulada?.EstrategiaUtilizada;
            set => SetEtiquetaGeneralProperty(e => e.EstrategiaUtilizada = value);
        }

        [Obsolete("Use la propiedad TipoCorrespondePorTramo de la etiqueta General")]
        public decimal? GeneralTipoCorrespondePorTramo
        {
            get => EtiquetaGeneralAcumulada?.TipoCorrespondePorTramo;
            set => SetEtiquetaGeneralProperty(e => e.TipoCorrespondePorTramo = value);
        }

        [Obsolete("Use la propiedad TipoRealmenteAplicado de la etiqueta General")]
        public decimal? GeneralTipoRealmenteAplicado
        {
            get => EtiquetaGeneralAcumulada?.TipoRealmenteAplicado;
            set => SetEtiquetaGeneralProperty(e => e.TipoRealmenteAplicado = value);
        }

        [Obsolete("Use la propiedad MotivoEstrategia de la etiqueta General")]
        public string MotivoEstrategia
        {
            get => EtiquetaGeneralAcumulada?.MotivoEstrategia;
            set => SetEtiquetaGeneralProperty(e => e.MotivoEstrategia = value);
        }

        [Obsolete("Use la propiedad ComisionSinEstrategia de la etiqueta General")]
        public decimal? GeneralComisionSinEstrategia
        {
            get => EtiquetaGeneralAcumulada?.ComisionSinEstrategia;
            set => SetEtiquetaGeneralProperty(e => e.ComisionSinEstrategia = value);
        }

        public bool TieneEstrategiaEspecial => EtiquetaGeneralAcumulada?.TieneEstrategiaEspecial ?? false;
        public bool EsSobrepago => EtiquetaGeneralAcumulada?.EsSobrepago ?? false;
    }
}