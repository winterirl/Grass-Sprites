import React, { useEffect, useRef, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button } from "cs2/ui";
import { ModRegistrar } from "cs2/modding";
import grassIcon from "images/grass-white.svg";
import brushIcon from "images/grass-sprites-brush.svg";
import pencilIcon from "images/grass-sprites-pencil.svg";
import styles from "./GrassSpritesButton.module.scss";

const bindingGroup = "GrassSprites";
const enabled$ = bindValue<boolean>(bindingGroup, "enabled", false);
const panelVisible$ = bindValue<boolean>(bindingGroup, "panelVisible", false);
const precisionMode$ = bindValue<boolean>(bindingGroup, "precisionMode", false);
const brushRadius$ = bindValue<number>(bindingGroup, "brushRadius", 40);

const minBrushRadius = 1;
const maxBrushRadius = 250;
const brushRadiusStep = 0.5;

const clamp = (value: number, min: number, max: number) => Math.min(max, Math.max(min, value));

const snapBrushRadius = (value: number) => {
  const snapped = Math.round(value / brushRadiusStep) * brushRadiusStep;
  return clamp(snapped, minBrushRadius, maxBrushRadius);
};

const BrushRadiusSlider = ({ value }: { value: number }) => {
  const trackRef = useRef<HTMLDivElement | null>(null);
  const [dragging, setDragging] = useState(false);
  const percent = ((clamp(value, minBrushRadius, maxBrushRadius) - minBrushRadius) / (maxBrushRadius - minBrushRadius)) * 100;

  const setFromClientX = (clientX: number) => {
    const track = trackRef.current;
    if (!track) {
      return;
    }

    const rect = track.getBoundingClientRect();
    if (rect.width <= 0) {
      return;
    }

    const t = clamp((clientX - rect.left) / rect.width, 0, 1);
    const nextValue = snapBrushRadius(minBrushRadius + t * (maxBrushRadius - minBrushRadius));
    trigger(bindingGroup, "setBrushRadius", nextValue);
  };

  useEffect(() => {
    if (!dragging) {
      return;
    }

    const onMove = (event: MouseEvent) => {
      event.preventDefault();
      setFromClientX(event.clientX);
    };

    const onUp = (event: MouseEvent) => {
      event.preventDefault();
      setDragging(false);
      setFromClientX(event.clientX);
    };

    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);

    return () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
    };
  }, [dragging]);

  return (
    <div className={styles.sliderWrap}>
      <div
        ref={trackRef}
        className={`${styles.sliderTrack} ${dragging ? styles.sliderTrackActive : ""}`}
        onMouseDown={(event) => {
          event.preventDefault();
          setDragging(true);
          setFromClientX(event.clientX);
        }}
      >
        <div className={styles.sliderFill} style={{ width: `${percent}%` }} />
        <div className={styles.sliderThumb} style={{ left: `${percent}%` }} />
      </div>
      <div className={styles.sliderBounds}>
        <span>{minBrushRadius} m</span>
        <span>{maxBrushRadius} m</span>
      </div>
    </div>
  );
};

const defaultPanelPosition = { x: 24, y: 740 };

const GrassSpritesPanel = () => {
  const enabled = useValue(enabled$);
  const panelVisible = useValue(panelVisible$);
  const precisionMode = useValue(precisionMode$);
  const brushRadius = useValue(brushRadius$);
  const panelRef = useRef<HTMLDivElement | null>(null);
  const [panelPosition, setPanelPosition] = useState(defaultPanelPosition);
  const [draggingPanel, setDraggingPanel] = useState(false);
  const dragOffsetRef = useRef({ x: 0, y: 0 });

  useEffect(() => {
    if (!draggingPanel) {
      return;
    }

    trigger(bindingGroup, "setPointerIsOverPanel", true);

    const onMove = (event: MouseEvent) => {
      event.preventDefault();
      const offset = dragOffsetRef.current;
      const panel = panelRef.current;
      const panelWidth = panel?.getBoundingClientRect().width ?? 360;
      const panelHeight = panel?.getBoundingClientRect().height ?? 180;
      const nextX = clamp(event.clientX - offset.x, 0, Math.max(0, window.innerWidth - panelWidth));
      const nextY = clamp(event.clientY - offset.y, 0, Math.max(0, window.innerHeight - panelHeight));
      setPanelPosition({ x: nextX, y: nextY });
    };

    const onUp = (event: MouseEvent) => {
      event.preventDefault();
      setDraggingPanel(false);

      const rect = panelRef.current?.getBoundingClientRect();
      const pointerInside =
        !!rect &&
        event.clientX >= rect.left &&
        event.clientX <= rect.right &&
        event.clientY >= rect.top &&
        event.clientY <= rect.bottom;

      trigger(bindingGroup, "setPointerIsOverPanel", pointerInside);
    };

    window.addEventListener("mousemove", onMove);
    window.addEventListener("mouseup", onUp);

    return () => {
      window.removeEventListener("mousemove", onMove);
      window.removeEventListener("mouseup", onUp);
    };
  }, [draggingPanel]);

  useEffect(() => {
    return () => trigger(bindingGroup, "setPointerIsOverPanel", false);
  }, []);

  useEffect(() => {
    if (!enabled || !panelVisible) {
      trigger(bindingGroup, "setPointerIsOverPanel", false);
    }
  }, [enabled, panelVisible]);

  if (!enabled || !panelVisible) {
    return null;
  }

  const radius = Math.round(brushRadius * 10) / 10;

  return (
    <div
      ref={panelRef}
      className={styles.brushPanel}
      style={{ left: `${panelPosition.x}px`, top: `${panelPosition.y}px` }}
      onMouseEnter={() => trigger(bindingGroup, "setPointerIsOverPanel", true)}
      onMouseLeave={() => {
        if (!draggingPanel) {
          trigger(bindingGroup, "setPointerIsOverPanel", false);
        }
      }}
    >
      <div
        className={styles.panelHeader}
        onMouseDown={(event) => {
          event.preventDefault();
          const rect = panelRef.current?.getBoundingClientRect();
          dragOffsetRef.current = {
            x: rect ? event.clientX - rect.left : 0,
            y: rect ? event.clientY - rect.top : 0,
          };
          setDraggingPanel(true);
          trigger(bindingGroup, "setPointerIsOverPanel", true);
        }}
      >
        <div className={styles.panelTitle}>Grass Sprites</div>
        <button
          className={styles.closeButton}
          onMouseDown={(event) => event.stopPropagation()}
          onClick={() => trigger(bindingGroup, "togglePanel")}
          title="Close"
        >
          ×
        </button>
      </div>

      <div className={styles.modeRow}>
        <button
          className={`${styles.modeButton} ${!precisionMode ? styles.modeButtonActive : ""}`}
          onClick={() => trigger(bindingGroup, "setNormalBrush")}
        >
          <img src={brushIcon} />
          <span>Brush</span>
        </button>
        <button
          className={`${styles.modeButton} ${precisionMode ? styles.modeButtonActive : ""}`}
          onClick={() => trigger(bindingGroup, "setPrecisionBrush")}
        >
          <img src={pencilIcon} />
          <span>Precision</span>
        </button>
      </div>

      <div className={styles.controlBlock}>
        {!precisionMode ? (
          <>
            <div className={styles.labelRow}>
              <span>Brush Radius</span>
              <span className={styles.valueText}>{radius.toFixed(1)} m</span>
            </div>
            <BrushRadiusSlider value={brushRadius} />
          </>
        ) : (
          <p className={styles.helpText}>Precision brush edits one mask pixel per stamp.</p>
        )}
      </div>
    </div>
  );
};

const GrassSpritesButton = () => {
  const enabled = useValue(enabled$);
  const panelVisible = useValue(panelVisible$);

  if (!enabled) {
    return null;
  }

  return (
    <div className={styles.grassSpritesRoot}>
      <Button
        src={grassIcon}
        title="Grass Sprites"
        variant="floating"
        className={`${styles.grassSpritesButton} ${panelVisible ? styles.selected : ""}`}
        onSelect={() => trigger(bindingGroup, "togglePanel")}
      />
      <GrassSpritesPanel />
    </div>
  );
};

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", GrassSpritesButton);
};

export default register;
