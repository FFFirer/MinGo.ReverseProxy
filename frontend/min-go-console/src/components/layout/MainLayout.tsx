import { JSX } from 'solid-js';

interface Props {
  children: JSX.Element;
}

export default function MainLayout(props: Props) {
  return <>{props.children}</>;
}
