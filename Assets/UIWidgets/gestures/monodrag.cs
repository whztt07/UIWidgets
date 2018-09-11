using System;
using System.Collections.Generic;
using UIWidgets.foundation;
using UIWidgets.ui;

namespace UIWidgets.gestures {
    enum _DragState {
        ready,
        possible,
        accepted,
    }

    public delegate void GestureDragEndCallback(DragEndDetails details);

    public delegate void GestureDragCancelCallback();

    public abstract class DragGestureRecognizer : OneSequenceGestureRecognizer {
        public DragGestureRecognizer(GestureBinding binding, object debugOwner = null)
            : base(binding: binding, debugOwner: debugOwner) {
        }

        public GestureDragDownCallback onDown;

        public GestureDragStartCallback onStart;

        public GestureDragUpdateCallback onUpdate;

        public GestureDragEndCallback onEnd;

        public GestureDragCancelCallback onCancel;

        public double? minFlingDistance;

        public double? minFlingVelocity;

        public double? maxFlingVelocity;

        _DragState _state = _DragState.ready;
        Offset _initialPosition;
        protected Offset _pendingDragOffset;
        DateTime _lastPendingEventTimestamp;

        protected abstract bool _isFlingGesture(VelocityEstimate estimate);
        protected abstract Offset _getDeltaForDetails(Offset delta);
        protected abstract double? _getPrimaryValueFromOffset(Offset value);
        protected abstract bool _hasSufficientPendingDragDeltaToAccept { get; }

        readonly Dictionary<int, VelocityTracker> _velocityTrackers = new Dictionary<int, VelocityTracker>();

        public override void addPointer(PointerDownEvent evt) {
            this.startTrackingPointer(evt.pointer);
            this._velocityTrackers[evt.pointer] = new VelocityTracker();
            if (this._state == _DragState.ready) {
                this._state = _DragState.possible;
                this._initialPosition = evt.position;
                this._pendingDragOffset = Offset.zero;
                this._lastPendingEventTimestamp = evt.timeStamp;
                if (this.onDown != null) {
                    this.invokeCallback<object>("onDown",
                        () => {
                            this.onDown(new DragDownDetails(globalPosition: this._initialPosition));
                            return null;
                        });
                }
            } else if (this._state == _DragState.accepted) {
                this.resolve(GestureDisposition.accepted);
            }
        }

        protected override void handleEvent(PointerEvent evt) {
            D.assert(this._state != _DragState.ready);
            if (!evt.synthesized
                && (evt is PointerDownEvent || evt is PointerMoveEvent)) {
                var tracker = this._velocityTrackers[evt.pointer];
                D.assert(tracker != null);
                tracker.addPosition(evt.timeStamp, evt.position);
            }

            if (evt is PointerMoveEvent) {
                Offset delta = evt.delta;
                if (this._state == _DragState.accepted) {
                    if (this.onUpdate != null) {
                        this.invokeCallback<object>("onUpdate", () => {
                            this.onUpdate(new DragUpdateDetails(
                                sourceTimeStamp: evt.timeStamp,
                                delta: this._getDeltaForDetails(delta),
                                primaryDelta: this._getPrimaryValueFromOffset(delta),
                                globalPosition: evt.position
                            ));
                            return null;
                        });
                    }
                } else {
                    this._pendingDragOffset += delta;
                    this._lastPendingEventTimestamp = evt.timeStamp;
                    if (this._hasSufficientPendingDragDeltaToAccept) {
                        this.resolve(GestureDisposition.accepted);
                    }
                }
            }

            this.stopTrackingIfPointerNoLongerDown(evt);
        }

        public override void acceptGesture(int pointer) {
            if (this._state != _DragState.accepted) {
                this._state = _DragState.accepted;
                Offset delta = this._pendingDragOffset;
                var timestamp = this._lastPendingEventTimestamp;
                this._pendingDragOffset = Offset.zero;
                this._lastPendingEventTimestamp = default(DateTime);
                if (this.onStart != null) {
                    this.invokeCallback<object>("onStart", () => {
                        this.onStart(new DragStartDetails(
                            sourceTimeStamp: timestamp,
                            globalPosition: this._initialPosition
                        ));
                        return null;
                    });
                }

                if (delta != Offset.zero && this.onUpdate != null) {
                    this.invokeCallback<object>("onUpdate", () => {
                        this.onUpdate(new DragUpdateDetails(
                            sourceTimeStamp: timestamp,
                            delta: this._getDeltaForDetails(delta),
                            primaryDelta: this._getPrimaryValueFromOffset(delta),
                            globalPosition: this._initialPosition
                        ));
                        return null;
                    });
                }
            }
        }

        public override void rejectGesture(int pointer) {
            this.stopTrackingPointer(pointer);
        }

        protected override void didStopTrackingLastPointer(int pointer) {
            if (this._state == _DragState.possible) {
                this.resolve(GestureDisposition.rejected);
                this._state = _DragState.ready;
                if (this.onCancel != null) {
                    this.invokeCallback<object>("onCancel", () => {
                        this.onCancel();
                        return null;
                    });
                }

                return;
            }

            bool wasAccepted = this._state == _DragState.accepted;
            this._state = _DragState.ready;
            if (wasAccepted && this.onEnd != null) {
                var tracker = this._velocityTrackers[pointer];
                D.assert(tracker != null);

                var estimate = tracker.getVelocityEstimate();
                if (estimate != null && this._isFlingGesture(estimate)) {
                    Velocity velocity = new Velocity(pixelsPerSecond: estimate.pixelsPerSecond)
                        .clampMagnitude(this.minFlingVelocity ?? Constants.kMinFlingVelocity,
                            this.maxFlingVelocity ?? Constants.kMaxFlingVelocity);
                    this.invokeCallback<object>("onEnd", () => {
                        this.onEnd(new DragEndDetails(
                            velocity: velocity,
                            primaryVelocity: this._getPrimaryValueFromOffset(velocity.pixelsPerSecond)
                        ));
                        return null;
                    }, debugReport: () =>
                        string.Format("{0}; fling at {1}.", estimate, velocity));
                } else {
                    this.invokeCallback<object>("onEnd", () => {
                            this.onEnd(new DragEndDetails(
                                velocity: Velocity.zero,
                                primaryVelocity: 0.0
                            ));
                            return null;
                        }, debugReport: () =>
                            estimate == null
                                ? "Could not estimate velocity."
                                : estimate + "; judged to not be a fling."
                    );
                }
            }

            this._velocityTrackers.Clear();
        }

        public override void dispose() {
            this._velocityTrackers.Clear();
            base.dispose();
        }
    }

    public class VerticalDragGestureRecognizer : DragGestureRecognizer {
        public VerticalDragGestureRecognizer(GestureBinding binding, Object debugOwner = null)
            : base(binding: binding, debugOwner: debugOwner) {
        }

        protected override bool _isFlingGesture(VelocityEstimate estimate) {
            double minVelocity = this.minFlingVelocity ?? Constants.kMinFlingVelocity;
            double minDistance = this.minFlingDistance ?? Constants.kTouchSlop;
            return Math.Abs(estimate.pixelsPerSecond.dy) > minVelocity && Math.Abs(estimate.offset.dy) > minDistance;
        }

        protected override bool _hasSufficientPendingDragDeltaToAccept {
            get { return Math.Abs(this._pendingDragOffset.dy) > Constants.kTouchSlop; }
        }

        protected override Offset _getDeltaForDetails(Offset delta) {
            return new Offset(0.0, delta.dy);
        }

        protected override double? _getPrimaryValueFromOffset(Offset value) {
            return value.dy;
        }

        public override string debugDescription {
            get { return "vertical drag"; }
        }
    }

    public class HorizontalDragGestureRecognizer : DragGestureRecognizer {
        public HorizontalDragGestureRecognizer(GestureBinding binding, Object debugOwner = null)
            : base(binding: binding, debugOwner: debugOwner) {
        }

        protected override bool _isFlingGesture(VelocityEstimate estimate) {
            double minVelocity = this.minFlingVelocity ?? Constants.kMinFlingVelocity;
            double minDistance = this.minFlingDistance ?? Constants.kTouchSlop;
            return Math.Abs(estimate.pixelsPerSecond.dx) > minVelocity && Math.Abs(estimate.offset.dx) > minDistance;
        }

        protected override bool _hasSufficientPendingDragDeltaToAccept {
            get { return Math.Abs(this._pendingDragOffset.dx) > Constants.kTouchSlop; }
        }

        protected override Offset _getDeltaForDetails(Offset delta) {
            return new Offset(delta.dx, 0.0);
        }

        protected override double? _getPrimaryValueFromOffset(Offset value) {
            return value.dx;
        }

        public override string debugDescription {
            get { return "horizontal drag"; }
        }
    }

    public class PanGestureRecognizer : DragGestureRecognizer {
        public PanGestureRecognizer(GestureBinding binding, Object debugOwner = null)
            : base(binding: binding, debugOwner: debugOwner) {
        }

        protected override bool _isFlingGesture(VelocityEstimate estimate) {
            double minVelocity = this.minFlingVelocity ?? Constants.kMinFlingVelocity;
            double minDistance = this.minFlingDistance ?? Constants.kTouchSlop;
            return estimate.pixelsPerSecond.distanceSquared > minVelocity * minVelocity
                   && estimate.offset.distanceSquared > minDistance * minDistance;
        }

        protected override bool _hasSufficientPendingDragDeltaToAccept {
            get { return this._pendingDragOffset.distance > Constants.kPanSlop; }
        }

        protected override Offset _getDeltaForDetails(Offset delta) {
            return delta;
        }

        protected override double? _getPrimaryValueFromOffset(Offset value) {
            return null;
        }

        public override string debugDescription {
            get { return "pan"; }
        }
    }
}